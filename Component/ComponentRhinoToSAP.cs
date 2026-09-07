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
        //静态引用：指向当前画布上的连接电池实例，供SyncEngine计时器刷新用
        public static ComponentRhinoToSAP Instance;
        // 实例标志：是否已完成首次连接
        private bool _isFirstConnection = false;

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
            // 已完成首次连接：计时器触发时只刷新输出，不重复执行连接逻辑
            if (_isFirstConnection)
            {
                RefreshOutput(DA);
                return;
            }
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
                _isFirstConnection = false;
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
            SAPConnector.UnitCheckMessage = report;

            //设置间隔
            SyncEngine.SyncInterval = intervalsSeconds * 1000;

            //初始化同步引擎(只注册Rhino事件，不启动Timer)
            SyncEngine.Initialize();

            //连接成功
            _isFirstConnection= true;
            string sapModelName = SAPConnector.SapModel.GetModelFilename(true);
            report = $"✅ 已完成连接，单位一致" +
                $"\n已绑定图层：{RLayer}，同步间隔：{SyncEngine.SyncInterval / 1000}秒" +
                $"\n已与SAP程序[{sapModelName}]连接" ;
           
            DA.SetData(0, report);
            DA.SetData(1, isConnected);
        }

        // 组件被拖入画布时，注册实例引用
        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            Instance = this;
        }
        // 组件从画布删除时，清空实例引用
        public override void RemovedFromDocument(GH_Document document)
        {
            base.RemovedFromDocument(document);
            Instance = null;
        }
        //计时器刷新：只更新输出文字，不碰连接逻辑
        private void RefreshOutput(IGH_DataAccess DA)
        {
            try
            {
                if (SAPConnector.IsConnected)
                {
                    TimeSpan duration = SAPConnector.ConnectDuration;
                    string durationText = $"{duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
                    string sapModelName = SAPConnector.SapModel.GetModelFilename(true);
                    DA.SetData(0, $"✅ {SAPConnector.UnitCheckMessage}" +
                        $"\n已绑定图层：{SAPConnector.RootLayerName}，同步间隔：{SyncEngine.SyncInterval / 1000}秒" +
                        $"\n已与SAP程序[{sapModelName}]连接{durationText}，已同步{SyncEngine.SyncCount}次");
                    DA.SetData(1, true);
                }
                else
                {
                    DA.SetData(0, "❌ SAP连接已断开，请重新连接");
                    DA.SetData(1, false);
                    _isFirstConnection = false;
                }
            }
            catch(Exception  ex)
            {
                // 清理并更新状态，避免遗留不可用的 COM 引用
                RhinoApp.WriteLine($"SAP连接状态刷新失败：{ex.Message}");
                DA.SetData(0, "❌ SAP连接已断开（RPC不可用），请重新连接");
                DA.SetData(1, false);
                _isFirstConnection = false;
                return;
            }
        }

        // 电池图标（暂时用null，后面可以自定义）
        protected override System.Drawing.Bitmap Icon => null;

        // 组件唯一GUID，不要改
        public override Guid ComponentGuid => new Guid("a6c97b83-b082-49f0-89d8-043b81b9397e");
    }
}
