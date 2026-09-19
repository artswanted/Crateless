using System.Globalization;

namespace GHelper.UI.Nebula
{
    /// <summary>
    /// Strings of the Nebula shell. Temporary: RU/EN inline until the keys are moved into
    /// Properties/Strings.resx (design kit stage E4). Keys follow implementation/strings.ru-en.json.
    /// </summary>
    public static class NebulaText
    {
        private static bool Ru
        {
            get
            {
                var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                return lang == "ru" || lang == "uk" || lang == "be";
            }
        }

        public static string T(string en, string ru) => Ru ? ru : en;

        public static string ControlCenter => T("Control Center", "Центр управления");
        public static string Tagline => T("Your laptop. Your rules.", "Твой ноутбук. Твои правила.");
        public static string OnAc => T("PLUGGED IN", "ОТ СЕТИ");
        public static string OnBattery => T("ON BATTERY", "ОТ БАТАРЕИ");
        public static string YourDevice => T("YOUR DEVICE", "ТВОЁ УСТРОЙСТВО");
        public static string Connected => T("CONNECTED", "ПОДКЛЮЧЕНО");
        public static string NotConnected => T("NO ASUS ACPI", "НЕТ ASUS ACPI");
        public static string Cpu => T("PROCESSOR", "ПРОЦЕССОР");
        public static string Gpu => T("GRAPHICS", "ГРАФИКА");
        public static string Load => T("Load", "Загрузка");
        public static string Power => T("Power", "Мощность");
        public static string Sleeping => T("sleeping", "спит");
        public static string Cooling => T("COOLING", "ОХЛАЖДЕНИЕ");
        public static string Configure => T("Configure  ↗", "Настроить  ↗");
        public static string CpuFan => "CPU FAN";
        public static string GpuFan => "GPU FAN";
        public static string SystemFan => "SYSTEM FAN";
        public static string Rpm => T("rpm", "об/мин");
        public static string Memory => T("MEMORY", "ПАМЯТЬ");
        public static string Gb => T("GB", "ГБ");
        public static string Watt => T("W", "Вт");
        public static string Mode => T("PERFORMANCE MODE", "РЕЖИМ РАБОТЫ");
        public static string SilentHint => T("Less noise", "Меньше шума");
        public static string BalancedHint => T("Everyday", "На каждый день");
        public static string TurboHint => T("More power", "Больше мощности");
        public static string CustomHint => T("Custom profile", "Свой профиль");
        public static string More => T("More…", "Ещё…");
        public static string QuickAccess => T("QUICK ACCESS", "БЫСТРЫЙ ДОСТУП");
        public static string GraphicsAuto => T("Graphics · automatic", "Графика · автоматический выбор");
        public static string GraphicsMode => T("Graphics · GPU mode", "Графика · режим GPU");
        public static string ScreenHint => T("Display · refresh rate", "Экран · частота обновления");
        public static string ScreenAuto => T("auto", "авто");
        public static string BatteryHint => T("Battery · charge limit", "Батарея · лимит заряда");
        public static string LightHint => T("Lighting · keyboard effect", "Подсветка · эффект клавиатуры");
        public static string SensorsUpdated => T("Sensors · updated", "Датчики · обновлено");
        public static string SensorsWaiting => T("Sensors · waiting for first read", "Датчики · ждём первое чтение");
        public static string LegacyWindow => T("Opens the classic window", "Откроется классическое окно");
        public static string Optimized => "Optimized";
        public static string Eco => "Eco";
        public static string Standard => "Standard";
        public static string Ultimate => "Ultimate";

        public static string RailOverview => T("Overview", "Обзор");
        public static string RailPower => T("Power", "Мощность");
        public static string RailFan => T("Cooling", "Охлаждение");
        public static string RailGpu => T("Graphics", "Графика");
        public static string RailDisplay => T("Display", "Экран");
        public static string RailBattery => T("Battery", "Батарея");
        public static string RailLight => T("Lighting", "Подсветка");
        public static string RailKeyboard => T("Keys", "Клавиши");
        public static string RailMouse => T("Devices", "Устройства");
        public static string RailSettings => T("Settings", "Настройки");
    }
}
