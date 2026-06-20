# MicGain MVP

Windows desktop utility that controls per-device microphone gain by writing `Preamp:` lines into Equalizer APO config files.

## How to use

1. **Download** this repo (Code → Download ZIP, or `git clone`)
2. **Run** `run.bat` (or double-click `app\MicGain.App.exe`)
3. If Equalizer APO is not installed, the app will offer to install it for you

## Requirements

- Windows 10 or later
- [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) (the app can install it for you)

## What's inside

```
app/              ← compiled app + all dependencies
  MicGain.App.exe
  assets/installer/EqualizerAPO-x64-1.4.2.exe
run.bat           ← launcher
README.md
```

## Build from source

Source code is in the `main` branch of the [original repository](https://gitlab.com/arrow1655269pablo-group/arrow1655269pablo-project).