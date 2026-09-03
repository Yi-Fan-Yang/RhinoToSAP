using CSiAPIv1;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoToSAP.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace RhinoToSAP.Sync
{
    public static class SyncEngine
    {
        // ========== 核心调度字段 ==========
        // 同步计时器
        public static Timer syncTimer;
        // SAP连接状态检测计时器，每隔5秒检测一次SAP是否还在运行
        public static Timer connectionCheckTimer;
        // 待处理变化列表：用HashSet自动去重，同一个对象修改多次只存一个ID
        public static HashSet<Guid> pendingChanges = new HashSet<Guid>();
        // 初始化标志位：避免重复注册事件、重复启动计时器
        public static bool isInitialized = false;
        // 增量同步暂停标志：用户放弃接续后暂停，重新连接时重置
        public static bool SyncPaused = false;

        // ========== 常量配置 ==========
        // 同步间隔，默认3000ms
        public static int _syncInterval = 3000;
        public static int SyncInterval
        {
            get => _syncInterval; 
            set
            {
                if (value < 1000) value = 1000; // 最小1000ms
                if(value > 30000) value = 30000; // 最大30000ms
                _syncInterval = value;
                // 如果计时器已经启动，同步更新计时器间隔
                if (syncTimer != null)
                {
                    syncTimer.Interval = _syncInterval;
                }
            }
        }

        // ========== 公共方法：对外提供的接口 ==========
        // 初始化同步引擎：注册Rhino事件、启动防抖计时器，只需要调用一次
        // 连接成功、单位校验通过、绑定图层后调用
        public static void Initialize()
        {
            if (isInitialized) return;
            SyncPaused = false;  // 重新连接时重置暂停标志
            try
            {
                // 注册Rhino对象事件：用户增删改对象时自动触发对应方法
                // RhinoDoc是Rhino的文档类，这些都是静态事件，直接+=订阅
                RhinoDoc.AddRhinoObject += SyncRhinoDispatcher.OnObjectAdded;       // 对象添加事件
                RhinoDoc.DeleteRhinoObject += SyncRhinoDispatcher.OnObjectDeleted;   // 对象删除事件
                RhinoDoc.ReplaceRhinoObject += SyncRhinoDispatcher.OnObjectReplaced; // 对象修改事件（移动、改坐标等都会触发）
                RhinoDoc.ModifyObjectAttributes += SyncRhinoDispatcher.OnObjectAttributesModified;//对象属性修改时间

                // 初始化同步计时器
                syncTimer = new Timer();
                syncTimer.Interval = _syncInterval; // 同步间隔
                syncTimer.Tick += OnSyncTimerTick; // 绑定定时触发的方法
                syncTimer.Start(); // 启动计时器

                // 初始化连接状态检测计时器
                connectionCheckTimer = new Timer();
                connectionCheckTimer.Interval = 5000;
                connectionCheckTimer.Tick += OnConnectionCheckTimerTick;
                connectionCheckTimer.Start();
                //标记已经初始化
                isInitialized = true;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"SyncEngine初始化失败：{ex.Message}");
            }
        }

        // 停止同步引擎：注销事件、停止计时器，插件卸载时调用
        public static void Shutdown()
        {
            if (!isInitialized) return;

            try
            {
                // 注销事件，和注册的时候一一对应，用-=
                RhinoDoc.AddRhinoObject -= SyncRhinoDispatcher.OnObjectAdded;
                RhinoDoc.DeleteRhinoObject -= SyncRhinoDispatcher.OnObjectDeleted;
                RhinoDoc.ReplaceRhinoObject -= SyncRhinoDispatcher.OnObjectReplaced;
                RhinoDoc.ModifyObjectAttributes -= SyncRhinoDispatcher.OnObjectAttributesModified;

                // 停止并释放同步计时器
                if (syncTimer != null)
                {
                    syncTimer.Stop();
                    syncTimer.Dispose();
                    syncTimer = null;
                }
                // 停止并释放连接状态检测计时器
                if (connectionCheckTimer != null)
                {
                    connectionCheckTimer.Stop();
                    connectionCheckTimer.Dispose();
                    connectionCheckTimer = null;
                }

                isInitialized = false;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"SyncEngine销毁失败：{ex.Message}");
            }
        }

        //全量同步
        public static void FullSync()
        {
            if (!SAPConnector.IsConnected|| (string.IsNullOrEmpty(SAPConnector.RootLayerName)   )) return;
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null) return;
            //初始化前清空状态，避免旧的映射关系干扰
            SyncStateManager.ClearState();
            //递归获取根图层下的所有子图层
            Layer rootlayer = doc.Layers.FindName(SAPConnector.RootLayerName);
            if (rootlayer == null) return; 
            List<Layer> layers = new List<Layer>();
            LayerHelper.GetAllChildLayers(rootlayer, layers);

            //遍历所有子图层，获取每个图层下的所有有效杆件
            foreach (Layer i in layers)
            {
                RhinoObject[] objs = doc.Objects.FindByLayer(i);
                if (objs == null) continue;
                foreach (RhinoObject obj in objs)
                {
                    if (SyncFrame.TryExplodePolylineAndQueue(doc, obj)) continue;
                    SyncRhinoDispatcher.AddToPendingIfValid(obj);
                }
            }
            RhinoApp.WriteLine($"[FullSync] 找到图层数量：{layers.Count}，待处理变化数量：{pendingChanges.Count}");
            // 遍历完所有图层后，立刻执行处理
            ProcessPendingChanges();
        }

        //手动同步：立即处理所有待处理列表，不等待计时器触发
        public static void ManualSync()
        {
            if (SyncPaused) return;  // 暂停中，不执行
            if (!isInitialized) return;
            syncTimer.Stop();//计时器停止并重置
            ProcessPendingChanges();//立即处理全部待处理列表
            syncTimer.Start();//计时器开始
        }


        // ----- 计时器触发时执行 -----
        // 计时器Tick事件：批量处理所有待处理的变化
        public static void OnSyncTimerTick(object sender, EventArgs e)
        {
                ProcessPendingChanges();
        }
        // SAP连接检测计时器触发方法
        public static void OnConnectionCheckTimerTick(object sender, EventArgs e)
        {
            if (SAPConnector.CheckConnectionAlive()) return;
            RhinoApp.WriteLine("[SyncEngine] SAP连接已断开，自动锁定图层并清空同步状态");
            SAPConnector.Disconnect();
        }

        //整体处理待处理列表
        public static void ProcessPendingChanges()
        {
            if (SyncPaused) return;  // 暂停中，不处理
            try
            {
                if (!SAPConnector.IsConnected
                    || (string.IsNullOrEmpty(SAPConnector.RootLayerName))
                    || (RhinoDoc.ActiveDoc == null))
                {
                    pendingChanges.Clear();
                    return;
                }
                Guid[] pengdingIds = pendingChanges.ToArray();
                if (pengdingIds.Length == 0) return;
                foreach (Guid i in pengdingIds)
                {
                    try
                    {
                        SyncRhinoDispatcher.ProcessSingleChange(RhinoDoc.ActiveDoc, i);
                    }
                    catch
                    {
                    }
                }
                pendingChanges.Clear();
            }
            catch
            {
                pendingChanges.Clear();
            }
        }




    }
}

