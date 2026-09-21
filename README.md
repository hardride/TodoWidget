# ADHD To-Do Widget

<p align="center">
  <img src="assets/preview-syntwave.png" alt="Syntwave Theme" width="240"/>
  <img src="assets/preview-pixel76.png" alt="Pixel-76 Theme" width="240"/>
  <img src="assets/preview-terminal.png" alt="Terminal Theme" width="240"/>
</p>

<p align="center">
  <a href="https://github.com/hardride/TodoWidget/releases/latest">
    <img src="https://img.shields.io/github/v/release/hardride/TodoWidget?style=for-the-badge&color=blue" alt="Latest Release"/>
  </a>
  <img src="https://img.shields.io/badge/Platform-Windows-0078D6?style=for-the-badge&logo=windows" alt="Platform Windows"/>
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 9.0"/>
</p>

Компактный, стильный и минималистичный виджет списка задач для рабочего стола Windows, созданный для быстрого фиксирования мыслей и фокуса на главном без отвлекающих факторов.

---

## ✨ Основные возможности

- **📌 Всегда под рукой (Always on Top)**: Закрепление поверх всех окон одной кнопкой.
- **🔳 Режим плитки (32×32)**: Сворачивание в компактную плавающую плитку, которую можно переместить в любой угол экрана.
- **👁 Регулировка прозрачности**: Настройка непрозрачности от 20% до 100% со слайдером. Автоматическое приглушение при потере фокуса.
- **🎨 Разнообразные темы оформления**:
  - **Syntwave** — неоновый ретро-вейв с розовым свечением текста.
  - **Pixel-76** — эстетика авиации с атмосферным пиксель-арт фоном.
  - **Terminal** — хакерский зелёный стиль с моноширинным шрифтом *Source Code Pro*.
  - **Amber** — тёплая янтарная палитра для глаз.
  - **Dark / Light** — классические минималистичные светлая и тёмная темы.
  - **Kanagawa** — японская природная цветовая гамма.
  - **Argentina for Plemyannic** — кастомная тема с фоновым артом.
- **🔔 Системный трей**: Быстрое сворачивание в трей, восстановление по двойному клику и контекстное меню.
- **⚡ Single Instance**: Защита от случайного запуска нескольких копий виджета.
- **💾 Сохранение данных**: Задачи и настройки темы сохраняются автоматически в `%LocalAppData%\TodoWidget\`.

---

## 🖼 Темы оформления

<p align="center">
  <img src="assets/preview-dark.png" alt="Dark Theme" width="220"/>
  <img src="assets/preview-amber.png" alt="Amber Theme" width="220"/>
  <img src="assets/preview-syntwave.png" alt="Syntwave Theme" width="220"/>
</p>

---

## 🚀 Скачать и запустить

1. Перейдите на страницу **[Последнего релиза](https://github.com/hardride/TodoWidget/releases/latest)**.
2. Скачайте архив `TodoWidget-vX.X.X.zip`.
3. Распакуйте в удобную папку и запустите `TodoWidget.exe` (установка не требуется).

---

## 🛠 Управление и горячие клавиши

- **Добавление задачи**: введите текст в поле снизу и нажмите <kbd>Enter</kbd> (или кнопку `+`).
- **Завершение задачи**: клик по чекбоксу слева.
- **Удаление задачи**: клик по крестику `✕` справа.
- **Выбор темы**: кнопка с кистью 🎨 в шапке виджета.
- **Прозрачность**: кнопка с кругами 👁.
- **Закрепить поверх окон**: кнопка с булавкой 📌.
- **Свернуть в трей**: кнопка `—` справа в шапке.

---

## 💻 Сборка из исходников

Требуется установленный [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
# Клонировать репозиторий
git clone https://github.com/hardride/TodoWidget.git
cd TodoWidget\TodoWidget

# Сборка и запуск Debug
dotnet run

# Сборка единого exe-файла (Release)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ../Publish
```
