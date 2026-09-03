using System;
using System.Windows.Forms;
using Rhino;
using Rhino.DocObjects;
using System.Runtime.InteropServices;
using CSiAPIv1;
using RhinoToSAP.Sync;
using RhinoToSAP.Tools;

namespace RhinoToSAP
{
    /// <summary>
    /// SAP连接单例，整个插件共用一个SAP实例，避免重复Attach
    /// </summary>
    public static class SAPConnector
    {
        private static cOAPI _sapApp;
        private static cSapModel _sapModel;

        /// 是否已连接到SAP实例
        public static bool IsConnected => _sapApp != null && _sapModel != null;

        /// SAP应用对象
        public static cOAPI SapApp => _sapApp;
        /// SAP模型对象
        public static cSapModel SapModel => _sapModel;
        ///Rhino根图层
        public static string RootLayerName { get; set; }= string.Empty;

        // 尝试连接到正在运行的SAP2000实例
        public static bool Connect(out string message)
        {
            try
            {
                // 已经连接过就直接返回
                if (IsConnected)
                {
                    message = "✅ 已连接到SAP2000实例";
                    return true;
                }

                // 附加到正在运行的SAP实例（新版ProgID）
                _sapApp = (cOAPI)Marshal.GetActiveObject("CSI.SAP2000.API.SapObject");
                if (_sapApp == null)
                {
                    message = "❌ 未找到正在运行的SAP2000实例，请先打开SAP2000并新建空白模型";
                    return false;
                }

                // 新版API：SapModel是属性，不是GetSapModel()方法
                _sapModel = _sapApp.SapModel;
                if (_sapModel == null)
                {
                    message = "❌ SAP实例已找到，但获取SapModel失败";
                    return false;
                }

                message = "✅ 成功连接到SAP2000实例";
                return true;
            }
            catch (COMException ex)
            {
                message = $"❌ COM连接异常：{ex.Message}（请确认Rhino和SAP都以管理员身份运行）";
                _sapApp = null;
                _sapModel = null;
                return false;
            }
            catch (Exception ex)
            {
                message = $"❌ 连接异常：{ex.Message}";
                _sapApp = null;
                _sapModel = null;
                return false;
            }
        }

        //检查Rhino和SAP的单位是否一致
        public static bool CheckUnits(RhinoDoc doc, out string message)
        {
            UnitSystem rhinoUnit = doc.ModelUnitSystem;
            eUnits sapUnit = _sapModel.GetPresentUnits();

            eUnits expectedSapUnit;
            switch(rhinoUnit)
            {
                case UnitSystem.Millimeters:
                    expectedSapUnit = eUnits.kN_mm_C;
                    break;
                case UnitSystem.Centimeters:
                    expectedSapUnit = eUnits.kN_cm_C;
                    break;
                case UnitSystem.Meters:
                    expectedSapUnit = eUnits.kN_m_C;
                    break;
                default:
                    message = $"❌ 连接成功，不支持的Rhino单位：{rhinoUnit}";
                    return false;
            }
            if (expectedSapUnit == sapUnit)
            {
                // 单位一致
                message = $"✅ 连接成功，单位一致，当前单位：{rhinoUnit}";
                return true;
            }
            else
            {
                // 单位不一致
                message = $"❌ 连接成功，单位不一致：Rhino单位是{rhinoUnit}，SAP单位是{sapUnit}，请统一单位";
                return false;
            }
        }

        // 检测SAP连接是否仍然存活（SAP关闭后会返回false）
        public static bool CheckConnectionAlive()
        {
            if (_sapApp == null || _sapModel == null) return false;
            try
            {
                // 调用一个最简单的API，能成功返回说明连接正常,用GetPresentUnit()，没有参数，调用最快
                _sapModel.GetPresentUnits();
                return true;
            }
            catch
            {
                // SAP关闭后调用COM对象会抛异常，说明连接已经断了
                return false;
            }
        }

        //断开连接（一般不用手动调用，关闭Rhino自动释放）
        public static void Disconnect()
        {
            try
            {
                // 先停止同步引擎，注销事件、释放所有计时器
                SyncEngine.Shutdown();

                // 锁定绑定的根图层（如果有活动文档和绑定图层）
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc != null && !string.IsNullOrEmpty(RootLayerName))
                {
                    LayerHelper.LockLayer(doc, RootLayerName);
                }

                // 清空绑定的图层名
                RootLayerName = string.Empty;

                // 清空SAP连接对象
                _sapModel = null;
                _sapApp = null;
            }
            catch
            {
                // 吞掉异常，避免断开过程中出错导致Rhino崩溃
            }
        }
    }
}