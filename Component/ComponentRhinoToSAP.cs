using Grasshopper.Kernel;
using System.IO;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using static System.Collections.Specialized.BitVector32;
using RhinoToSAP.Sync;
using RhinoToSAP.Tools;

namespace RhinoToSAP.Component
{
    public class ComponentRhinoToSAP : GH_Component
    {
        // 构造函数：定义电池名称、分类
        public ComponentRhinoToSAP()
          : base("SAP连接确认", "SAPConnect","确认是否成功连接到正在运行的SAP2000实例","RtoS", "Start")
        {
        }
        // 注册输入端口
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Layer", "L", "传导入SAP的图层名称", GH_ParamAccess.item);
            pManager.AddTextParameter("FrameSection", "F", "框架截面", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Seconds", "S", "Interval in 1~30 seconds", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Trigger", "T", "设为True开始连接SAP2000", GH_ParamAccess.item, false);
        }

        // 注册输出端口
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("状态报告", "Report", "连接状态详细信息", GH_ParamAccess.item);
            pManager.AddBooleanParameter("是否连接", "IsConnected", "SAP2000是否连接成功", GH_ParamAccess.item);
        }

        // 核心执行逻辑
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string RLayer = string.Empty;
            string FrameSection = string.Empty;
            int intervalsSeconds = 3;
            bool trigger = false;
            DA.GetData(0, ref RLayer);
            DA.GetData(1, ref FrameSection);
            DA.GetData(2, ref intervalsSeconds);
            DA.GetData(3, ref trigger);

            string report;
            bool isConnected;

            //校验1：Layer不能为空
            if (RLayer == string.Empty)
            {
                DA.SetData(0, "❌ Layer不能为空");
                DA.SetData(1, false);
                return;
            }
            
            //校验2：检查图层是否存在
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            Layer rootLayer = doc.Layers.FindName(RLayer);
            SyncFrame.FrameSection = FrameSection;
            if (rootLayer == null)
            {
                DA.SetData(0, "❌ 未找到对应图层");
                DA.SetData(1, false);
                return;
            }

            //触发开关
            if (!trigger)
            {
                SAPConnector.Disconnect();
                report = "⏳ 已断开连接，图层已锁定";
                DA.SetData(0, report);
                DA.SetData(1, false);
                return;
            }

            //进行sap连接
            isConnected = SAPConnector.Connect(out report);
            
            //判断连接是否成功，如果不成功则清空RootLayerName并锁定图层
            if (!isConnected)
            {
                SAPConnector.RootLayerName=string.Empty;
                LayerHelper.LockLayer(doc,RLayer);
                DA.SetData(0, report);
                DA.SetData(1, false);
                return;
            }

            //连接成功后检查单位制并解锁图层
            if (!SAPConnector.CheckUnits(doc, out report))
            { 
                SAPConnector.RootLayerName = string.Empty;
                LayerHelper.LockLayer(doc, RLayer);
                DA.SetData(0, report);
                DA.SetData(1, false);
                return;
            }
            SAPConnector.RootLayerName = RLayer;
            LayerHelper.UnLockLayer(doc, RLayer);
            
            //设置间隔
            SyncEngine.SyncInterval = intervalsSeconds * 1000;

            //初始化同步引擎(仅第一次调用会初始化)
            SyncEngine.Initialize();
            //拿取映射文件路径
            string mapPath=SyncPersistenceIO.GetMappingFilePath();
            if (!string.IsNullOrEmpty(mapPath) && File.Exists(mapPath))
            {
                var result
                    = Rhino.UI.Dialogs.ShowMessage(
                        $"检测到映射文件：{mapPath}\n是否加载？\n若不加载，将会创建新的映射文件。", "映射文件加载",
                        Rhino.UI.ShowMessageButton.YesNo,
                        Rhino.UI.ShowMessageIcon.Question);
                if(result == Rhino.UI.ShowMessageResult.Yes)
                {
                    if (!SyncContinueManager.TryContinue(out string errorMsg))
                    {
                        report = $"❌ 映射文件加载失败：{errorMsg}";
                        DA.SetData(0, report);
                        DA.SetData(1, false);
                        return;
                    }
                    report =$"✅ 已恢复上次映射并完成增量对齐,单位一致，已绑定图层：{RLayer}，同步间隔：{SyncEngine.SyncInterval / 1000}秒";
                }
                else
                {
                    //用户放弃接续：暂停后续所有增量同步（自动+手动）
                    SyncEngine.SyncPaused = true;
                    if(SyncEngine.syncTimer != null)
                    {
                        SyncEngine.syncTimer.Stop();
                    }
                    report = "⚠️ 已放弃接续恢复，增量同步已暂停，请重新连接以恢复";
                }
            }
            else
            {
                report = $"✅ 连接成功，单位一致，已绑定图层：{RLayer}，同步间隔：{SyncEngine.SyncInterval / 1000}秒";
            }
         
            DA.SetData(0, report);
            DA.SetData(1, isConnected);
        }

        // 电池图标（暂时用null，后面可以自定义）
        protected override System.Drawing.Bitmap Icon => null;

        // 组件唯一GUID，不要改
        public override Guid ComponentGuid => new Guid("a6c97b83-b082-49f0-89d8-043b81b9397e");
    }
}
