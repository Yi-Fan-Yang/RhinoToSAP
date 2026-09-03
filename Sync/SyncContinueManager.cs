using System;
using Rhino;
using RhinoToSAP.Data;

namespace RhinoToSAP.Sync
{
    /// <summary>
    /// 接续流程调度类：串起文件读取、校验、增量对齐、同步执行、保存的完整流程
    /// 是整个接续功能的唯一对外入口，SyncEngine只需要调用这里的TryContinue方法
    /// </summary>
    public static class SyncContinueManager
    {
        // ========== 公共方法：唯一对外入口 ==========

        /// <summary>
        /// 尝试执行映射接续，任何环节失败都直接终止，不自动全量同步
        /// </summary>
        /// <param name="errorMsg">接续失败时的错误信息，成功时为空</param>
        /// <returns>接续成功返回true，失败返回false</returns>
        public static bool TryContinue(out string errorMsg)
        {
            errorMsg = string.Empty;

            try
            {
                // 步骤1：从磁盘读取映射文件到内存
                if (!SyncPersistenceIO.LoadMapping(out string loadError))
                {
                    errorMsg = $"映射文件读取失败：{loadError}";
                    return false;
                }

                // 步骤2：执行校验和增量对齐，生成待处理列表
                if (!SyncContinueValidator.ValidateAndAlign(out string validateError))
                {
                    errorMsg = $"接续校验失败：{validateError}";
                    // 校验失败，清空已经加载的部分映射，避免脏数据
                    SyncStateManager.ClearState();
                    return false;
                }

                // 步骤3：执行待处理列表（更新坐标、创建、删除）
                ExecutePendingChanges();

                // 步骤4：保存最新的映射文件
                SyncPersistenceIO.SaveMapping();

                // 接续成功
                RhinoApp.WriteLine("[SyncContinue] 映射接续成功，已完成增量对齐");
                return true;
            }
            catch (Exception ex)
            {
                // 任何异常都终止，清空状态，不自动全量同步
                errorMsg = $"接续过程发生异常：{ex.Message}";
                SyncStateManager.ClearState();
                RhinoApp.WriteLine($"[SyncContinue] 接续失败：{errorMsg}");
                return false;
            }
        }


        // ========== 私有辅助方法 ==========

        /// <summary>
        /// 执行校验生成的三个待处理列表
        /// </summary>
        private static void ExecutePendingChanges()
        {
            // Frame单元
            foreach (LineState state in SyncContinueValidator.PendingCreate)
            {
                SyncFrame.AddFrame(state);
            }
            foreach (LineState state in SyncContinueValidator.PendingUpdate)
            {
                SyncFrame.UpdateFrame(state);
            }
            foreach (Guid rhinoId in SyncContinueValidator.PendingDelete)
            {
                SyncFrame.DeleteFrame(rhinoId);
            }
            //Area单元
            //图层变化
            foreach (LayerChangeInfo change in SyncContinueValidator.PendingLayerChange)
            {
                if (SyncStateManager.TryGetMapping(change.RhinoId, out string sapId))
                {
                    SyncSapGeneral.UpdateSapObjectGroup(sapId, change.OldLayerFullPath, change.NewLayerFullPath);
                }
            }
        }
    }
}
