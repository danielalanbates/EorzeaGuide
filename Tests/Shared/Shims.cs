// Test-only stand-ins for the few Dalamud types the shared plugin code touches.
namespace Dalamud.Plugin.Services
{
    public interface IDataManager
    {
        Lumina.Excel.ExcelSheet<T> GetExcelSheet<T>() where T : struct, Lumina.Excel.IExcelRow<T>;
        Lumina.Excel.SubrowExcelSheet<T> GetSubrowExcelSheet<T>() where T : struct, Lumina.Excel.IExcelSubrow<T>;
    }
}
namespace Dalamud.Configuration { public interface IPluginConfiguration { int Version { get; set; } } }
namespace EorzeaGuide
{
    static class Plugin { public static readonly TestLog Log = new(); }
    class TestLog
    {
        public void Error(Exception e, string m) => Console.Error.WriteLine(m + ": " + e.Message);
        public void Warning(Exception e, string m) => Error(e, m);
    }

    /// IDataManager over a plain Lumina GameData.
    public sealed class LuminaData(Lumina.GameData game) : Dalamud.Plugin.Services.IDataManager
    {
        public Lumina.Excel.ExcelSheet<T> GetExcelSheet<T>() where T : struct, Lumina.Excel.IExcelRow<T> => game.GetExcelSheet<T>(Lumina.Data.Language.English)!;
        public Lumina.Excel.SubrowExcelSheet<T> GetSubrowExcelSheet<T>() where T : struct, Lumina.Excel.IExcelSubrow<T> => game.GetSubrowExcelSheet<T>(Lumina.Data.Language.English)!;
    }
}
