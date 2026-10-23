using System.Collections.Generic;

namespace TodoWidget.Services;

public static class LocalizationService
{
    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["en"] = new()
        {
            ["Settings"] = "Settings",
            ["Close"] = "Close",
            ["TabGeneral"] = "General",
            ["TabOpacity"] = "Opacity",
            ["TabThemes"] = "Themes",
            ["TabAbout"] = "About",
            ["Tasks"] = "Tasks",
            ["CompletedTasks"] = "Completed tasks",
            ["CompletedTasksDesc"] = "Keep in place when unchecked",
            ["Language"] = "Language",
            ["OpacityLevel"] = "Opacity level",
            ["WindowBehavior"] = "Window behavior",
            ["OnLostFocus"] = "On lost focus",
            ["OnLostFocusDesc"] = "Transparent when inactive",
            ["ThemesHeader"] = "Themes",
            ["AboutHeader"] = "About",
            ["AboutDesc"] = "Minimalist desktop todo widget for focus.",
            ["AddTaskPlaceholder"] = "Add a task...",
            ["SettingsTooltip"] = "Settings",
            ["HideSettingsTooltip"] = "Hide settings",
            ["CompletedFormat"] = "Completed ({0})",
            ["ClearAll"] = "Clear all",
            ["MusicTooltipOpen"] = "Focus music",
            ["MusicTooltipClose"] = "Close player",
            ["PinTooltipPin"] = "Pin on top of all windows",
            ["PinTooltipUnpin"] = "Unpin (on top of all windows)",
            ["MinimizeToTray"] = "Minimize to tray",
            ["FocusMusic"] = "Focus Music",
            ["AddDeadline"] = "title"
        },
        ["ru"] = new()
        {
            ["Settings"] = "Настройки",
            ["Close"] = "Закрыть",
            ["TabGeneral"] = "Основные",
            ["TabOpacity"] = "Прозрачность",
            ["TabThemes"] = "Темы",
            ["TabAbout"] = "О программе",
            ["Tasks"] = "Задачи",
            ["CompletedTasks"] = "Завершённые задачи",
            ["CompletedTasksDesc"] = "Остаются на месте при отключении",
            ["Language"] = "Язык",
            ["OpacityLevel"] = "Уровень прозрачности",
            ["WindowBehavior"] = "Поведение окна",
            ["OnLostFocus"] = "При потере фокуса",
            ["OnLostFocusDesc"] = "Прозрачность при неактивности",
            ["ThemesHeader"] = "Темы",
            ["AboutHeader"] = "О программе",
            ["AboutDesc"] = "Минималистичный виджет задач для концентрации.",
            ["AddTaskPlaceholder"] = "Добавить задачу...",
            ["SettingsTooltip"] = "Настройки",
            ["HideSettingsTooltip"] = "Скрыть настройки",
            ["CompletedFormat"] = "Завершенные ({0})",
            ["ClearAll"] = "Очистить всё",
            ["MusicTooltipOpen"] = "Музыка для концентрации",
            ["MusicTooltipClose"] = "Закрыть плеер",
            ["PinTooltipPin"] = "Закрепить поверх всех окон",
            ["PinTooltipUnpin"] = "Открепить (поверх всех окон)",
            ["MinimizeToTray"] = "Свернуть в трей",
            ["FocusMusic"] = "Музыка для фокуса",
            ["AddDeadline"] = "title"
        },
        ["es"] = new()
        {
            ["Settings"] = "Ajustes",
            ["Close"] = "Cerrar",
            ["TabGeneral"] = "General",
            ["TabOpacity"] = "Opacidad",
            ["TabThemes"] = "Temas",
            ["TabAbout"] = "Acerca de",
            ["Tasks"] = "Tareas",
            ["CompletedTasks"] = "Tareas completadas",
            ["CompletedTasksDesc"] = "Mantener en su lugar al desmarcar",
            ["Language"] = "Idioma",
            ["OpacityLevel"] = "Nivel de opacidad",
            ["WindowBehavior"] = "Comportamiento de ventana",
            ["OnLostFocus"] = "Al perder el foco",
            ["OnLostFocusDesc"] = "Transparente al estar inactivo",
            ["ThemesHeader"] = "Temas",
            ["AboutHeader"] = "Acerca de",
            ["AboutDesc"] = "Widget minimalista de tareas para la concentración.",
            ["AddTaskPlaceholder"] = "Añadir tarea...",
            ["SettingsTooltip"] = "Ajustes",
            ["HideSettingsTooltip"] = "Ocultar ajustes",
            ["CompletedFormat"] = "Completadas ({0})",
            ["ClearAll"] = "Limpiar todo",
            ["MusicTooltipOpen"] = "Música para concentrarse",
            ["MusicTooltipClose"] = "Cerrar reproductor",
            ["PinTooltipPin"] = "Fijar sobre todas las ventanas",
            ["PinTooltipUnpin"] = "Desfijar de primer plano",
            ["MinimizeToTray"] = "Minimizar a la bandeja",
            ["FocusMusic"] = "Música para concentrarse",
            ["AddDeadline"] = "title"
        }
    };

    public static string Get(string key, string lang = "en")
    {
        if (string.IsNullOrEmpty(lang) || !Strings.ContainsKey(lang)) lang = "en";
        if (Strings[lang].TryGetValue(key, out var val)) return val;
        if (Strings["en"].TryGetValue(key, out var fallback)) return fallback;
        return key;
    }

    public static string Get(string key, string lang, params object[] args)
    {
        var template = Get(key, lang);
        try
        {
            return string.Format(template, args);
        }
        catch
        {
            return template;
        }
    }
}
