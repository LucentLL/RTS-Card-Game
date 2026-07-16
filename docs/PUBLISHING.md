# Publishing — Steam (Tauri v2) + Google Play (Capacitor 7)

Spawn Row Duel is a vanilla-JS web game (`index.html` + `src/`). Store builds wrap a
single-file **dist** bundle in native shells, mirroring DriverCity (`LucentLL/Racing-Game-2`):
Tauri v2 for desktop/Steam, Capacitor 7 for Android/Play. Same `appId` on both stores.

## 1. Prerequisites

- **Node 18+** — Capacitor CLI and Tauri CLI (`npm i -D @tauri-apps/cli @capacitor/cli @capacitor/core @capacitor/android`).
- **Rust** (rustup, stable) — Tauri only. Windows also needs MSVC Build Tools + WebView2 (preinstalled on Win10/11).
- **Android Studio** + **JDK 17** + Android SDK API 35+ (`ANDROID_HOME` set) — Capacitor only.
- **Python 3** — `tools/build.py`.

The two toolchains are independent; installing one is enough to ship that platform.

## 2. Build the dist bundle

```sh
py tools/build.py        # -> dist/spawn-row-duel.html (inlines src/styles + src/js per tools/build_manifest.json)
```

Then copy runtime assets next to it (build.py does NOT do this yet):

```sh
cp dist/spawn-row-duel.html dist/index.html
cp -r assets dist/assets                     # card art: assets/cards/<slug>_cardart.<ext>, elements, sleeves, sprites
```

The shells load `dist/` as local files — no server. `sw.js`/`manifest.webmanifest` are for
GitHub Pages only; leave them out of dist (service workers are pointless in a native webview).

## 3. Desktop / Steam — Tauri v2

```sh
npx tauri init           # creates src-tauri/
npx tauri dev            # run windowed
npx tauri build          # installers in src-tauri/target/release/bundle/
```

`src-tauri/tauri.conf.json`, mirroring DriverCity's:

```json
{
  "$schema": "https://schema.tauri.app/config/2",
  "productName": "Spawn Row Duel",
  "identifier": "com.lucentll.spawnrowduel",
  "build": { "frontendDist": "../dist", "beforeBuildCommand": "py tools/build.py" },
  "app": { "windows": [{ "title": "Spawn Row Duel", "width": 1280, "height": 720, "resizable": true }],
           "security": { "csp": null } },
  "bundle": { "active": true, "targets": "all", "category": "Game" }
}
```

(No dev server exists in this repo, so omit `devUrl`/`beforeDevCommand` or point them at
`npx serve .`.) Steam side: create the app in Steamworks ($100 fee), then upload the
`bundle/` output with SteamCMD — one depot per OS, a simple app build script pointing at the
bundle dir, launch option = the executable. Steam Deck runs the Linux or Windows(Proton) build.

## 4. Android / Google Play — Capacitor 7

`capacitor.config.ts` (DriverCity's, retargeted):

```ts
import type { CapacitorConfig } from '@capacitor/cli';
const config: CapacitorConfig = {
  appId: 'com.lucentll.spawnrowduel',
  appName: 'Spawn Row Duel',
  webDir: 'dist',
  server: { androidScheme: 'https' },
  android: { allowMixedContent: false, backgroundColor: '#000000' },
};
export default config;
```

```sh
npx cap add android      # once: generates android/
py tools/build.py        # + asset copy (step 2) after every game change
npx cap sync             # copies dist/ into android/app/src/main/assets/public/
npx cap open android     # Android Studio: Run on device, or Build > Generate Signed Bundle
```

Play requires a signed **.aab** (keystore via Android Studio — back it up, losing it is
unrecoverable) and a $25 one-time developer account.

## 5. Store requirements checklist

| Item | Steam | Google Play |
|---|---|---|
| Icons | 32px–256px + library capsules (460x215, 600x900, 616x353, header) | 512px hi-res + adaptive icon (foreground/background layers in `android/.../res/`) |
| Screenshots | 5+ at 1920x1080 | 2+ phone screenshots, 1024x500 feature graphic |
| Signing | none (SteamCMD auth) | keystore-signed .aab, Play App Signing enrollment |
| Ratings | Steam questionnaire | IARC questionnaire |
| Other | depot/build script, launch options per OS | privacy policy URL, data-safety form, content rating |

`icon.svg` in the repo root is the master — rasterize per-store sizes from it.

## 6. Not done yet

- No `package.json`, `src-tauri/`, or `capacitor.config.ts` in this repo — all scaffolding above is TODO.
- `tools/build.py` doesn't copy `assets/` into `dist/` or emit `dist/index.html` — do it manually or extend the script.
- No raster icons/splash/screenshots; no keystore; no Steamworks or Play Console accounts wired up.
- Multiplayer rendezvous uses ntfy.sh over the network — confirm it works inside the shells (CSP is null in Tauri, and Capacitor needs it reachable over https) and declare network use in Play's data-safety form.
- Touch input is exercised on Pages; native-webview input (Tauri WebView2 pointer events, Android back button) is untested.
