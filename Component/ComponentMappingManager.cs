using Grasshopper.Kernel;
using Rhino;
using RhinoToSAP.MappingFile;
using RhinoToSAP.Sync;
using System;
using System.Drawing.Text;
using System.Windows.Forms;

namespace RhinoToSAP.Component
{
    public class ComponentMappingManager : GH_Component
    {
        // 构造函数：定义电池名称、分类
        public ComponentMappingManager()
          : base("MappingManager", "LNS",
                 "映射文件的加载、新建和保存",
                 "RtoS", "Start")
        {
        }

        // 注册输入端口：3个独立按钮
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Load", "L", "点击加载已有映射文件", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("New", "N", "点击新建空映射文件", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Save", "S", "点击保存当前映射文件", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Save As", "SA", "点击另存为当前映射文件", GH_ParamAccess.item, false);
        }

        // 注册输出端口
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Report", "R", "操作结果信息", GH_ParamAccess.item);
            pManager.AddTextParameter("Path", "P", "当前映射文件路径", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Ready", "B", "映射文件是否已加载成功", GH_ParamAccess.item);
        }
        // 汇总输出数据
        private static string report = "⏳ 等待操作（点击上方按钮）";
        private static string filePath = SyncPersistenceIO.CurrentMappingFilePath;
        private static bool isReady = SyncEngine.IsMappingLoaded;

        // 核心执行逻辑
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 读取输入
            bool load = false, createNew = false, save = false, saveAs = false;
            DA.GetData(0, ref load);
            DA.GetData(1, ref createNew);
            DA.GetData(2, ref save);
            DA.GetData(3, ref saveAs);

            // 文件操作
            if (load) MappingFileManager.LoadWithDialog();
            if (createNew) MappingFileManager.NewWithDialog();
            if (save) MappingFileManager.Save();
            if (saveAs) MappingFileManager.SaveAsWithDialog();

            //  输出
            DA.SetData(0, MappingFileManager.report);
            DA.SetData(1, MappingFileManager.filePath);
            DA.SetData(2, MappingFileManager.isReady);
        }

        // 电池图标
        protected override System.Drawing.Bitmap Icon => null;

        // 组件唯一GUID
        public override Guid ComponentGuid => new Guid("4DA71CAC-5249-4AD5-B673-25E5C11F78B5");
    }
}