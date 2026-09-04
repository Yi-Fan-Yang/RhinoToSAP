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
        // 计时器
        public static Timer Timer;

        // 增量同步计数器：数到目标值就执行一次同步，然后归零
        private static int _syncCounter = 0;
        // 连接检测计数器：数到5就检查一次SAP连接，然后归零
        private static int _connCheckCounter = 0;

        public static HashSet<Guid> pendingChanges = new HashSet<Guid>();
        
        // 初始化标志位：避免重复注册事件、重复启动计时器
        public static bool isInitialized = false;
        
        // 增量同步暂停标志：用户放弃接续后暂停，重新连接时重置
        public static bool SyncPaused = false;

        // ========== 常量配置 ==========
        // 同步间隔，默认3000ms
        private static int _syncInterval = 3000;
        public static int SyncInterval
        {
            get => _syncInterval; 
            set
            {
                if (value < 1000) value = 1000; // 最小1000ms
                if(value > 60000) value = 60000; // 最大60000ms
                _syncInterval = value;
            }
        }
        // 同步次数计数器：每次执行ProcessPendingChanges就+1
        private static int _syncCount = 0;
        public static int SyncCount => _syncCount;

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
                RhinoDoc.BeginSaveDocument += SyncRhinoDispatcher.OnRhinoDocumentSaved;//文档保存事件


                // 初始化计时器
                Timer = new Timer();
                Timer.Interval = 1000;
                Timer.Tick += OnTimerTick;
                Timer.Start();
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
                RhinoDoc.BeginSaveDocument -= SyncRhinoDispatcher.OnRhinoDocumentSaved;

                // 停止并释放同步计时器
                if (Timer != null)
                {
                    Timer.Stop();
                    Timer.Dispose();
                    Timer = null;
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
            ProcessPendingChanges();//立即处理全部待处理列表
            _syncCounter = 0;//手动同步后归零
        }


        // ----- 计时器触发时执行 -----
        // 计时器Tick事件：批量处理所有待处理的变化
        public static void OnTimerTick(object sender, EventArgs e)
        {
            // 每秒叫醒连接电池，让它刷新输出（连接时长、同步次数、IsConnected）
            RhinoToSAP.Component.ComponentRhinoToSAP.Instance?.ExpireSolution(true);

            // 两个计数器各自+1
            _syncCounter++;
            _connCheckCounter++;

            // 计数器1：数到同步间隔就执行增量同步
            int syncTarget = SyncInterval / 1000; // 转换为秒
            if (_syncCounter >= syncTarget)
            {
                ProcessPendingChanges();
                _syncCounter = 0; // 重置计数器
            }
            
            // 计数器2：数到5就检查SAP连接
            if (_connCheckCounter >= 5)
            {
                if(!SAPConnector.CheckConnectionAlive())
                {
                    RhinoApp.WriteLine("[SyncEngine] SAP连接已断开，自动锁定图层");
                    SAPConnector.Disconnect();
                }
                _connCheckCounter = 0; // 重置计数器
            }
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
                _syncCount++;
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

