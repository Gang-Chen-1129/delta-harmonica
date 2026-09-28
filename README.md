# Delta Harmonica / 三角洲口琴 · Windows portable app

English | [简体中文使用说明](三角洲口琴-使用说明.md)

Delta Harmonica is a Windows 11 desktop app for organizing harmonica scores, importing MIDI melodies, and playing notes into the currently focused window through keyboard and mouse input. The portable package includes its .NET runtime; extract the whole archive and run `DeltaHarmonica.exe` without installing .NET or Python. The app's interface is currently in Chinese.

**[Download the Windows x64 portable release](https://github.com/Gang-Chen-1129/delta-harmonica/releases/latest)** · [Release notes and SHA-256](三角洲口琴-发布记录.md)

The executable is not code signed, so Windows SmartScreen may show an unknown publisher warning on first launch.

## Getting started

1. Extract the entire archive to a writable folder, such as Desktop or Documents. Keep the files together rather than copying only the EXE.
2. Run `DeltaHarmonica.exe` and accept the Windows administrator prompt. The app reads `.txt` scores from the adjacent `songs` folder. The public package contains an original scale example only.
3. In Settings, calibrate the MIDI pitch assigned to `Z` for your target instrument. The default is 60 (middle C). You can also change transposition, playback speed, and the global hotkey.
4. Choose a score, review or edit its notes, and select Start Playback. You have three seconds to focus the receiving window. The default global hotkey is `F8`: it starts playback after about 6.5 seconds, cancels during the countdown, and stops during playback. You can bind another key or a `Ctrl`/`Alt`/`Shift` combination in Settings; press `Esc` to cancel binding.
5. On the Import MIDI page, select a local `.mid` or `.midi` file. If several tracks contain notes, choose the melody track. The app saves a playable monophonic TXT score and a copy of the original MIDI in `songs`. Review and edit the result in the score editor.

The library supports folders as categories, instant title search, adjustable pane width, and moving songs between categories. Existing files directly under `songs` appear as uncategorized. Deleting a selected song from the library moves its TXT score and matching MIDI copy to the Windows Recycle Bin after confirmation. Deleting selected notes in the score editor affects only those notes.

Import your own MIDI files only when you have the right to use them. Personal songs are excluded from the public source and release packages.

## Input and compatibility

The default note keys are `Z X C V B N M ,`. Holding the left mouse button lowers an octave; the right button raises an octave; the middle button raises a semitone. The app presses a required mouse modifier before the note key and releases both when the note ends or playback stops.

Playback uses Windows `SendInput` to send keyboard scan codes and mouse press/release events. The configurable hotkey uses Windows hotkey and keyboard observation APIs. The app does not attach to another process, read or write its memory, or inject DLLs. MIDI import reads only the file you select and does not upload it.

Administrator privileges let the app send input to a target window that is also elevated. If a countdown appears but the target receives no notes, try borderless window mode, switch to an English keyboard input method, and focus the target again. Exclusive fullscreen applications or applications that accept only hardware/Raw Input may ignore `SendInput`. The on-screen toast is a normal always-on-top window and may be hidden behind exclusive fullscreen content.

## Score format

Scores are UTF-8 text files with one note per line:

```text
# delta-harmonica v1
# title=Scale example
# start_ms,midi,duration_ms
0,60,250
300,62,250
600,64,250
```

The fields are start time, MIDI pitch, and duration, with times in milliseconds. Gaps are rests. Scores currently support one non-overlapping melodic line. The GUI can edit these values.

MIDI import reads standard format 0/1 note events and global tempo changes. It ignores the drum channel, skips silence at the beginning by default, and keeps later note spacing. When a selected track contains chords, it favors the highest note at a shared start time to produce one melody; inspect polyphonic results manually. The original MIDI file is not modified.

## Build from source

Install the .NET 10 SDK. In Windows PowerShell, run:

```powershell
./build-windows.ps1
```

The script publishes a self-contained Windows x64 build and creates `dist/三角洲口琴-v<version>-Windows-x64-便携版.zip`. It bundles only the repository's scale example, even if you have imported other songs locally. WinForms can be cross-compiled on other systems with Windows targeting enabled, but running the app and sending input requires Windows. Core smoke tests can be run with `dotnet run --project tests/CoreSmoke/CoreSmoke.csproj`.

## License

The project source is licensed under [MIT](LICENSE). Imported MIDI and score files remain subject to their respective owners' rights.
