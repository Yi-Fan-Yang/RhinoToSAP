using Grasshopper.Kernel;
using RhinoToSAP.Sync;
using System;
using System.Collections.Generic;


namespace RhinoToSAP.Component
{
    public class ComponentFullSync : GH_Component
    {
        public ComponentFullSync()
          : base("SAP全量同步", "SAPFullSync", "执行Rhino到SAP的全量同步操作", "RtoS", "Model")
        {
        }
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {

            pManager.AddBooleanParameter("Boolean", "B", "设为True开始全量同步操作", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Message", "M", "反馈信息", GH_ParamAccess.item);
            pManager.AddTextParameter("List", "L", "杆件数量统计", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool trigger = false;
            DA.GetData(0, ref trigger);
            // 如果没有触发，则输出等待信息
            if (!trigger)
            {
                DA.SetData(0, "⏳ 等待触发，设为True执行全量同步");
                DA.SetDataList(1, new List<string>() );
                return;
            }
            // 检查SAP连接状态
            if (!SAPConnector.IsConnected)
            {
                DA.SetData(0, "❌ 请先完成SAP连接");
                DA.SetDataList(1, new List<string>() );
                return;
            }
            //执行全量同步 + 统计
            int beforeCount = SyncStateManager.HistoryStatesCount;
            SyncEngine.FullSync();
            SyncPersistenceIO.SaveMapping();
            int afterCount = SyncStateManager.HistoryStatesCount;
            // 输出同步完成信息
            string message = $"✅ 全量同步完成，当前杆件总数：{afterCount}";
            List<string> details = new List<string>()
            {
                $"同步前：{beforeCount}根",
                $"同步后：{afterCount}根",
                $"变化：{Math.Abs(afterCount - beforeCount)}根"
            };
            DA.SetData(0, message); 
            DA.SetDataList(1, details);
        }
        // 电池图标
        protected override System.Drawing.Bitmap Icon => null;

        // 组件唯一GUID，不要改
        public override Guid ComponentGuid => new Guid("EF4C28C5-3F50-4D56-AD7D-DAA111DF31DD");
    }
}
