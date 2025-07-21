# 🧩 Floatie

**A smart, draggable floating widget system for organizing your desktop by file type, task, or project — with live content, modular design, and modern UX.**

---

## 📌 Overview

**Floatie** is a WPF-based desktop utility that brings life to your file organization. Instead of cluttering your screen with static icons, Floatie creates **floating, resizable, draggable panels ("Floaties")** for different file types or use cases.

Each Floatie acts like a mini dashboard — clean, visual, and interactive. Floatie is inspired by Stardock Fences, but built with a modular, programmable, and user-first mindset.

---

## ✨ Key Features

- 🪟 **Multiple Floatie Windows** — One window per file type (PDF, Images, Scripts, etc.), cleanly separated
- 🎛️ **Draggable Panels** — Intuitive drag-to-move UI with corner radius and transparency
- ❎ **Exit Button** — Close any Floatie instantly with a top-right ✖
- 🖼️ **File-Type Icons** — Each Floatie displays a relevant icon based on its type
- 📁 **Directory-Aware Architecture** *(in progress)* — Floaties can reflect file contents from actual folders
- 🎯 **Lightweight & Modular** — Minimal dependencies, flexible C# and XAML architecture

---

## 🏗️ Architecture

- `MainWindow.xaml` — Central launcher interface
- `FloatieWindow.xaml` — Reusable window template for each Floatie
- `Assets/` — Icons for different file types (PDF, folder, images, etc.)

Each FloatieWindow is dynamically instantiated from `MainWindow` based on file type definitions.

---

## 🚧 Roadmap

- [x] Multi-floatie window spawning from launcher
- [x] Drag-to-move and exit button
- [ ] Live file scanning per floatie (e.g., all `.pdf` in `~/Downloads`)
- [ ] File preview on hover (text, image, etc.)
- [ ] Snap-to-grid behavior for floaties
- [ ] Floatie themes (Dark/Light, minimal, glassy)
- [ ] Plugin system for power users
- [ ] Git-aware floatie panel (dev mode)

---

## 🛠️ Getting Started

1. Clone the repository:
   ```bash
   git clone https://github.com/yourusername/floatie.git
