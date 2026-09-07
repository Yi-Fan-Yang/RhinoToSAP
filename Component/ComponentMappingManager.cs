using Grasshopper.Kernel;
using RhinoToSAP.Sync;
using System;
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

        // 核心执行逻辑
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 1. 读取三个按钮输入
            bool load = false, createNew = false, save = false, saveAs = false;
            DA.GetData(0, ref load);
            DA.GetData(1, ref createNew);
            DA.GetData(2, ref save);
            DA.GetData(3, ref saveAs);

            string report = "⏳ 等待操作（点击上方按钮）";
            string filePath = SyncPersistenceIO.CurrentMappingFilePath;
            bool isReady = SyncEngine.IsMappingLoaded;

            // 2. 加载按钮
            if (load)
            {
                OpenFileDialog openDlg = new OpenFileDialog
                {
                    Filter = "映射文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    Title = "打开映射文件"
                };
                if (openDlg.ShowDialog() == DialogResult.OK)
                {
                    string selectedPath = openDlg.FileName;
                    if (SyncPersistenceIO.LoadMapping(selectedPath, out string errorMsg))
                    {
                        SyncEngine.IsMappingLoaded = true;
                        SyncEngine.UpdateTimerState();
                        report = $"✅ 映射文件加载成功: {selectedPath}";
                        filePath = selectedPath;
                        isReady = true;
                    }
                    else
                    {
                        SyncEngine.IsMappingLoaded = false;
                        SyncEngine.UpdateTimerState();
                        report = $"❌ 映射文件加载失败: {errorMsg}";
                        isReady = false;
                    }
                }
                else
                {
                    report = "❌ 映射文件加载取消";
                }
            }


            // 3. 新建按钮
            if (createNew)
            {
                SaveFileDialog saveDlg = new SaveFileDialog();
                saveDlg.Filter = "映射文件 (*.json)|*.json";
                saveDlg.Title = "新建映射文件";
                if (saveDlg.ShowDialog() == DialogResult.OK)
                {
                    string newPath = saveDlg.FileName;
                    if (SyncPersistenceIO.CreateEmptyMapping(newPath))
                    {
                        // 新建后自动加载
                        SyncPersistenceIO.LoadMapping(newPath, out string errorMsg);
                        SyncEngine.IsMappingLoaded = true;
                        SyncEngine.UpdateTimerState();
                        report = $"✅ 新建映射文件成功: {newPath}";
                        filePath = newPath;
                        isReady = true;
                    }
                    else
                    {
                        SyncEngine.IsMappingLoaded = false;
                        SyncEngine.UpdateTimerState();
                        report = $"❌ 新建映射文件失败";
                        isReady = false;
                    }
                }
                else
                {
                    report = "❌ 新建映射文件取消";
                }
            }

            // 4. 保存按钮
            if (save)
            {
                if (SyncEngine.IsMappingLoaded)
                {
                    if (SyncPersistenceIO.SaveMapping())
                    {
                        report = $"✅ 映射文件保存成功: {SyncPersistenceIO.CurrentMappingFilePath}";
                    }
                    else
                    {
                        report = $"❌ 映射文件保存失败";
                    }
                }
                else
                {
                    report = "❌ 没有加载的映射文件，无法保存";
                }
            }
            // 5. 另存为按钮
            if (saveAs)
            {
                if (SyncEngine.IsMappingLoaded)
                {
                    SaveFileDialog saveDlg = new SaveFileDialog();
                    saveDlg.Filter = "映射文件 (*.json)|*.json";
                    saveDlg.Title = "另存为映射文件";
                    if (saveDlg.ShowDialog() == DialogResult.OK)
                    {
                        string newPath = saveDlg.FileName;
                        if (SyncPersistenceIO.SaveMappingAs(newPath))
                        {
                            report = $"✅ 映射文件另存为成功: {newPath}";
                            filePath = newPath;
                        }
                        else
                        {
                            report = $"❌ 映射文件另存为失败";
                        }
                    }
                }
                else
                {
                    report = "❌ 没有加载的映射文件，无法另存为";
                }
            }
            // 6. 输出
            DA.SetData(0, report);
            DA.SetData(1, filePath);
            DA.SetData(2, isReady);
        }

        // 电池图标
        protected override System.Drawing.Bitmap Icon => null;

        // 组件唯一GUID
        public override Guid ComponentGuid => new Guid("4DA71CAC-5249-4AD5-B673-25E5C11F78B5");
    }
}