using System;
using System.Windows.Forms;
using Rhino;
using RhinoToSAP.Sync;

namespace RhinoToSAP.MappingFile
{
    public static class MappingFileManager
    {
        // ========== 统一返回结果 ==========
        // 所有文件操作方法都返回这个对象，电池只管读取显示
        public static string report=string.Empty;// 状态报告
        public static string filePath = string.Empty;// 当前文件路径
        public static bool isReady = false;// 是否就绪

        // ========== 私有方法 ==========
        // 回滚：清空刚加载的内容，回到未加载状态
        private static void Rollback()
            {
                SyncPersistenceIO.UnLoadMapping();
                SyncEngine.IsMappingLoaded = false;
                SyncEngine.UpdateTimerState();
            }

        // 标记就绪：设置加载状态，启动Timer
        private static void MarkReady()
            {
                SyncEngine.IsMappingLoaded = true;
                SyncEngine.UpdateTimerState();
            }

        
        //========== 公共方法 ==========
        //新建映射文件
        public static void NewWithDialog()
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
        //打开已有文件
        public static void LoadWithDialog()
        {
            OpenFileDialog openDlg = new OpenFileDialog();
            openDlg.Filter = "映射文件 (*.json)|*.json|所有文件 (*.*)|*.*";
            openDlg.Title = "打开映射文件";

            if (openDlg.ShowDialog() != DialogResult.OK)
            {
                report = "❌ 映射文件加载取消";
                return;
            }

            string selectedPath = openDlg.FileName;
            //加载文件到内存，不成功报错返回
            if (!SyncPersistenceIO.LoadMapping(selectedPath, out string errorMsg1))
            {
                Rollback();                
                report = $"❌ 映射文件加载失败: {errorMsg1}";
                isReady = false;
                return;
            }
            //加载成功后进行校验
            if (!MappingValidator.Validate(out string errorMsg2))
            {
                Rollback();
                report = $"❌ 映射文件校验失败: {errorMsg2}";
                isReady = false;
                return;
            }

            //校验成功后弹窗查询是否有变动，无变动则完成加载
            if(MappingValidator.PendingUpdate==null&& MappingValidator.PendingDelete==null)
            {
                MarkReady();
                report = $"✅ 映射文件加载成功: {selectedPath}";
                filePath = selectedPath;
                isReady = true;
                return;
            }

            //如有变动，弹窗询问是否进行增量对齐
            var result = Rhino.UI.Dialogs.ShowMessage(
                "检测到rhino有变化，是否立刻进行增量对齐", "增量对齐",
                Rhino.UI.ShowMessageButton.YesNo,
                Rhino.UI.ShowMessageIcon.Question);

            //选NO则取消操作，回滚
            if(result==Rhino.UI.ShowMessageResult.No)
            {
                Rollback();
                report = $"❌ 用户不进行对齐，打开操作取消";
                isReady = false;
                return;
            }

            //选Yes则进行增量对齐
            MappingValidator.ExecutePendingChanges();
            MarkReady();
            report = $"✅ 映射文件加载成功: {selectedPath}";
            filePath = selectedPath;
            isReady = true;
            return;
        }
        //保存文件
        public static void SaveWithDialog()
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
        //另存为文件
        public static void SaveAsWithDialog()
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
    }
}
