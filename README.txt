========================================
  ADHD TO-DO WIDGET — Инструкция
  Версия: 1.0.3
========================================

1. ЗАПУСК
---------
Способ 1: Ярлык на рабочем столе "Todo Widget"
Способ 2: Двойной клик по TodoWidget.exe в папке Publish
Способ 3: Автозапуск — виджет стартует с Windows (ярлык в shell:startup)

Папка данных: %LocalAppData%\TodoWidget\
  - todos.json — список задач
  - settings.json — сохранённая тема


2. ОСНОВНОЙ ФУНКЦИОНАЛ
-----------------------
- Ввод задачи: написать текст + Enter или кнопка "+"
- Отметить выполнение: кликнуть чекбокс слева
- Удалить задачу: кликнуть "✕" справа
- Счётчик: показывает выполненные/все (справа от заголовка)


3. ЗАГОЛОВОК — ИКОНКИ СЛЕВА НАПРАВО
--------------------------------------
  🎨 Paintbrush  — выбор темы (список тем)
  👁 Blend       — слайдер прозрачности (20%-100%)
  📌 Pin         — закрепить поверх всех окон (вкл/выкл)
  ↩ Arrow-down   — свернуть в плитку 32x32 на рабочем столе


4. РЕЖИМ ПЛИТКИ
----------------
- Клик по arrow-down-to-line → виджет превращается в плитку 32x32
- Плитку можно таскать по экрану
- Клик по плитке → виджет восстанавливается
- Плитка становится полупрозрачной при потере фокуса


5. ПРОЗРАЧНОСТЬ
----------------
- Кнопка blend → появляется слайдер
- Регулирует прозрачность виджета от 20% до 100%
- При потере фокуса виджет становится 40% прозрачным


6. ТЕМЫ
--------
Текущие темы:
  - Dark (по умолчанию)
  - Light
  - Kanagawa
  - Argentina for Plemyannic (с фоновым изображением)
  - Terminal (шрифт Source Code Pro)
  - Reilly

Выбор темы: клик по paintbrush → список тем → клик по теме.
Тема сохраняется и восстанавливается при перезапуске.


7. СИСТЕМНЫЙ ТРЕЙ
------------------
- Виджет сворачивается в трей кнопкой "—" (minimize)
- Двойной клик по иконке в трее → восстановление
- Правый клик → меню: Показать / Выход


8. СБОРКА ИЗ ИСХОДНИКОВ
--------------------------
Требования:
  - .NET 9 SDK (https://dotnet.microsoft.com/download)

Команды:
  cd TodoWidget\TodoWidget
  dotnet build          — сборка Debug
  dotnet run            — запуск
  dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o Publish — сборка exe


9. ПУБЛИКАЦИЯ НА GITHUB
-------------------------
Репозиторий: https://github.com/hardride/TodoWidget

Новая версия:
  1. Изменить версию в TodoWidget.csproj (AssemblyVersion, FileVersion)
  2. Закоммитить:
     git add -A
     git commit -m "vX.X.X: описание"
  3. Создать тег и запушить:
     git tag vX.X.X
     git push --tags

GitHub Actions автоматически соберёт exe и создаст Release.


10. СТРУКТУРА ПРОЕКТА
----------------------
TodoWidget/
├── Fonts/                    — шрифты (Inter, Source Code Pro)
├── Icons/                    — SVG иконки Lucide
├── Images/
│   ├── bg_a.png              — фон для Argentina темы
│   └── btn.png               — иконка плитки и трея
├── Models/
│   └── TodoItem.cs           — модель задачи
├── Services/
│   ├── TodoService.cs        — CRUD задач (JSON)
│   ├── SettingsService.cs    — сохранение настроек
│   └── ThemeService.cs       — (зарезервировано)
├── Themes/
│   ├── Dark.xaml             — тёмная тема
│   ├── Light.xaml            — светлая тема
│   ├── Kanagawa.xaml
│   ├── Argentina for Plemyannic.xaml
│   ├── Terminal.xaml         — шрифт Source Code Pro
│   └── Reilly.xaml
├── MainWindow.xaml           — основной UI
├── MainWindow.xaml.cs        — логика виджета
├── TileWindow.xaml.cs        — окно плитки 32x32
├── App.xaml
└── TodoWidget.csproj


11. ЦВЕТА ТЕМ (переменные DynamicResource)
-------------------------------------------
WidgetBg        — фон виджета
TaskBg          — фон задачи / поле ввода
TextPrimary     — основной текст
TextSecondary   — вторичный текст, зачёркнутая задача
Accent          — активный чекбокс, кнопка "+"
AccentHover     — ховер на accent
DeleteFg        — крестик удаления
CheckboxBg      — пустой чекбокс
CheckboxHoverBg — ховер на чекбокс
IconHoverBg     — ховер на иконки хедера, фон слайдера
InputBg         — фон поля ввода
HeaderIconColor — цвет иконок хедера (non-active)
BorderColor     — обводка виджета
FontRegular     — основной шрифт темы
FontSemiBold    — полужирный шрифт темы


12. ИСПОЛЬЗУЕМЫЕ ИКОНКИ (Lucide)
----------------------------------
  check       — галочка в чекбоксе
  plus        — кнопка добавления задачи
  x           — крестик удаления задачи / закрытия
  blend       — прозрачность
  pin         — закрепить
  pin-off     — открепить
  paintbrush  — выбор темы
  arrow-down-to-line — свернуть в плитку

========================================
