# StretchCord

**Stream 4:3 stretched games to Discord in 16:9 with game audio support.**

StretchCord is a lightweight Windows app made for people who play games in **stretched resolution** and want their Discord stream to look the same way they see it, instead of appearing boxed, square, or in the original 4:3 aspect ratio.

It captures a selected game window, shows it stretched in a separate stream window, and forwards the game audio so Discord can transmit it correctly.

---

## Features

- Capture a selected game window
- Display 4:3 gameplay stretched to 16:9
- Open a clean stream window made for Discord sharing
- Capture the selected game's audio
- Choose a separate playback output to avoid hearing the game twice
- Lightweight Windows desktop app
- Useful for games like VALORANT, CS2, Fortnite, and other titles played in stretched res

---

## Why StretchCord exists

When you play a game in **4:3 stretched**, your monitor/GPU stretches the image locally.

However, when you stream the game on Discord, viewers often see:

- a square or boxed 4:3 image
- black bars
- the non-stretched version of the game

StretchCord solves this by creating a separate **16:9 stretched output window** that you can stream directly to Discord.

---

## Requirements

- Windows 10 or Windows 11
- .NET 8 Runtime or SDK
- A game running in **Windowed** or **Borderless Windowed** mode
- Discord desktop app

> Fullscreen exclusive games may not capture correctly. Borderless/windowed mode is recommended.

---

## How to use StretchCord

### 1. Open your game

Launch the game you want to stream.

For best results:

- Set the game to your preferred stretched resolution, such as `1280x960`, `1024x768`, etc.
- Use **Borderless Windowed** or **Windowed** mode if available.

---

### 2. Open StretchCord


StretchCord.exe

<img width="463" height="668" alt="{E7E1B917-C120-416A-9259-1E7C31FD7FFA}" src="https://github.com/user-attachments/assets/189f6a51-4fe1-4772-aae7-428899f94906" />

