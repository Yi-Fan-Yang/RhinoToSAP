using CSiAPIv1;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Components;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoToSAP.Sync;
using System;
using System.Collections.Generic;

namespace RhinoToSAP.Component
{
    public class ComponentUpdateElement : GH_Component
    {
        public ComponentUpdateElement()
          : base("update element", "U","立刻执行一次同步","RtoS", "Model")
        {
        }
        // 注册输入参数
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Button", "B", "立即同步", GH_ParamAccess.item, false);
        }

        // 注册输出参数
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Massage", "M", "反馈", GH_ParamAccess.item);
            pManager.AddTextParameter("List", "L", "杆件数量统计", GH_ParamAccess.list);
            // 例如：pManager.AddTextParameter("Result", "R", "处理结果", GH_ParamAccess.item);
        }



        // 组件执行主体（当前为空实现，按需实现逻辑）
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool Trigger = false;
            DA.GetData(0, ref Trigger);

            if (!Trigger)
            {
                DA.SetData(0, "⏳ 等待触发，设为True立即同步");
                DA.SetDataList(1, new List<string>());
                return;
            }

            if (string.IsNullOrEmpty(SyncFrame.FrameSection))
            {
                DA.SetData(0, "❌ 截面名不能为空");
                DA.SetDataList(1, new List<string>());
                return;
            }

            if (!SAPConnector.IsConnected || string.IsNullOrEmpty(SAPConnector.RootLayerName))
            {
                DA.SetData(0, "❌ 请先在连接电池完成SAP连接和图层绑定");
                DA.SetDataList(1, new List<string>());
                return;
            }

            // 记录同步前的数量，用于统计
            int beforeCount = SyncStateManager.HistoryStatesCount; // 后面加个公共属性

            // 执行手动同步
            SyncEngine.ManualSync();

            // 统计结果
            int afterCount = SyncStateManager.HistoryStatesCount;
            string message = $"✅ 同步完成，当前杆件总数：{afterCount}";
            List<string> details = new List<string>
            {
                $"同步前杆件数：{beforeCount}",
                $"同步后杆件数：{afterCount}",
                $"新增/修改/删除：{Math.Abs(afterCount - beforeCount)}根"
            };

            DA.SetData(0, message);
            DA.SetDataList(1, details);
        }

        // 电池图标（暂时用null，后面可以自定义）
        protected override System.Drawing.Bitmap Icon => null;

        // 必需的唯一标识符，换成你的稳定 GUID
        public override Guid ComponentGuid => new Guid("D1B3F8E2-4C9A-4A7E-9F2E-1234567890AB"); 
    }
}
