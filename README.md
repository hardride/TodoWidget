# ADHD to-do Widget

<p align="center">
  <img src="assets/preview.png" alt="ADHD to-do Widget" width="320"/>
</p>

<p align="center">
  <a href="https://github.com/hardride/TodoWidget/releases/latest/download/TodoWidget.exe">
    <img src="https://img.shields.io/badge/Скачать_exe-TodoWidget.exe-2ea44f?style=for-the-badge&logo=windows&logoColor=white" alt="Скачать exe"/>
  </a>
  <a href="https://github.com/hardride/TodoWidget/releases/latest">
    <img src="https://img.shields.io/github/v/release/hardride/TodoWidget?style=for-the-badge&color=blue" alt="Latest Release"/>
  </a>
  <img src="https://img.shields.io/badge/Platform-Windows-0078D6?style=for-the-badge&logo=windows" alt="Platform Windows"/>
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 9.0"/>
</p>

Компактный, стильный и минималистичный виджет списка задач для рабочего стола Windows, созданный для быстрого фиксирования мыслей и фокуса на главном без отвлекающих факторов.

---

## 🚀 Как скачать готовую программу

> ⚠️ **Важно:** Зелёная кнопка **`<> Code` → `Download ZIP`** на GitHub скачивает *исходный код* для разработчиков, а не готовую программу.

Чтобы скачать готовый виджет:
1. Нажмите прямую ссылку: **[Скачать TodoWidget.exe](https://github.com/hardride/TodoWidget/releases/latest/download/TodoWidget.exe)**
2. Либо перейдите в раздел **[Releases (Релизы)](https://github.com/hardride/TodoWidget/releases/latest)** (в правой колонке репозитория) и в блоке **Assets** скачайте `TodoWidget.exe` или `TodoWidget-v1.0.4.zip`.
3. Запустите скачанный файл — установка не требуется!

---

## ✨ Основные возможности

- **📌 Всегда под рукой (Always on Top)**: Закрепление поверх всех окон одной кнопкой.
- **🔳 Режим плитки (32×32)**: Сворачивание в компактную плавающую плитку, которую можно переместить в любой угол экрана.
- **👁 Регулировка прозрачности**: Настройка непрозрачности от 20% до 100% со слайдером. Автоматическое приглушение при потере фокуса.
- **🎨 Темы оформления**: Syntwave (со свечением текста), Pixel-76 (с пиксель-арт фоном), Amber, Terminal (шрифт *Source Code Pro*), Kanagawa, Dark, Light, Argentina for Plemyannic.
- **🔔 Системный трей**: Быстрое сворачивание в трей, контекстное меню и восстановление по двойному клику.
- **⚡ Single Instance**: Защита от случайного запуска нескольких копий виджета.
- **💾 Сохранение данных**: Задачи и настройки темы сохраняются автоматически в `%LocalAppData%\TodoWidget\`.

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
