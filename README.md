# RiqMenu

A simple RIQ menu for Bits & Bops. Currently for Windows only, but MacOS/Linux support is planned very soon.

## Quick Install (Windows)

Download and run `RiqMenuInstaller.exe` from the [latest release](https://github.com/KabirAcharya/RiqMenu/releases) - it will automatically:
- Find your Bits & Bops installation
- Install the latest BepInEx 5.x
- Install the latest RiqMenu
- Create the songs folder for you

## Manual Installation

- Install [BepInEx 5.x](https://docs.bepinex.dev/articles/user_guide/installation/index.html) into Bits & Bops.
- Place `RiqMenu.dll` into `BepInEx\plugins\`
- Place your `.riq`/`.bop` files into `Bits & Bops_Data\StreamingAssets\RiqMenu` (created automatically on first launch).

## Usage

- Launch the game and select `Custom Songs` from the title screen or press `F1` to toggle the menu.
- Navigate songs with `↑`/`↓` arrow keys or `W`/`S` keys
- Use `Page Up`/`Page Down` for faster navigation
- Press `Enter` or `Space` to select and play a song
- Press `P` to toggle autoplay mode (shows green/red status in menu)
- Press `Escape` or `F1` to close the menu
- Press `F2` to stop audio preview

## Thanks

- [ZeppelinGames](https://github.com/ZeppelinGames) for adding the title screen button and audio previews.
