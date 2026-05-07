# Tranzx Vibration PHM System V1.0 — 操作說明書

> **版本**：V1.0
> **發行日**：2026/05/04
> **適用對象**：機台振動監測現場操作員、保養工程師
> **語系**：繁體中文 / English / 日本語（即時切換）

---

## 1. 系統概述

Tranzx Vibration PHM System V1.0 是針對 Tranzx iVS-4 加速度感測器設計的桌面端即時監測系統。提供多 sensor 並行採樣、振動 / 角度 / 環境三類即時監控、警報、智慧錄製、歷史分析、3D 動態回放等功能，輸出符合 PHM (Prognostics & Health Management) 工作流程的標準資料。

**V1.0 核心定位**：**即時監測 + 趨勢記錄**。適合一般振動監控、警報觸發、現場巡檢與簡易回顧。

如需做轉子診斷、軸承故障特徵頻率分析、FFT 頻譜分析，請使用 V2.0。

---

## 2. 系統需求

| 項目 | 最低 |
|---|---|
| OS | Windows 10 (1903 以上) / Windows 11 |
| .NET | .NET 8 Runtime（首次執行會提示安裝） |
| CPU | Intel i3 6 代以上 / AMD Ryzen 3 |
| RAM | 4 GB（建議 8 GB） |
| 硬碟 | 安裝 200 MB；資料儲存依使用量，建議 5 GB 以上 |
| USB | 每個 sensor 占用一個 USB CDC 虛擬 COM port |

---

## 3. 安裝與啟動

1. 解壓縮 `Tranzx PHM V1.zip` 到任意資料夾
2. 連接 iVS-4 sensor 到電腦 USB
3. 執行 `Tranzx.iVS4.exe`
4. 首次啟動會自動建立資料夾 `%LocalAppData%\Tranzx PHM\`

---

## 4. 主畫面工具列

```
[+新增 Sensor] [-移除]  Sensor 同步 [▶ 啟動全部] [■ 停止全部] [● 同步錄製]
                                           ｜
        ☷📊📈📂🧊⚙ [ 語系 ▾ ]
        ↑ ↑ ↑ ↑ ↑ ↑
        │ │ │ │ │ └─ 偏好設定
        │ │ │ │ └─── 3D 動態回放
        │ │ │ └───── 歷史分析
        │ │ └─────── 振動統計（趨勢圖表）
        │ └───────── 警報統計
        └─────────── Sensor Dashboard（多感測器卡片總覽）

下方再加： [⚠ 軟體狀況監控] (Event Log)
```

---

## 5. 功能模組詳述

### 5.1 多 Sensor 連線管理

- 支援同時連接最多 4 個 iVS-4 sensor
- 自動偵測 USB 拔插（30 秒內自動重連）
- 每個 sensor 獨立採樣率 / 量程 / 警報門檻

**新增 Sensor**：工具列「+新增 Sensor」→ 選 COM port → 設名稱 → 連線

### 5.2 即時監測介面（每個 Sensor 一個分頁）

每個 sensor 分頁含三個子模式切換：

#### 振動模式
- **Trend View**：X/Y/Z 三軸 Peak / RMS 趨勢圖
- **Waveform View**：原始時域波形（短時段，可設定長度 1024 / 4096 / 16384 / 65536 / 204800 點）
- **FFT View**：即時頻譜（自動視窗函數）

#### 水平角度模式
- 三軸傾斜角即時顯示（度）

#### 環境模式
- 溫度（°C）
- 濕度（%）

### 5.3 警報系統

**振動警報**（綠 / 黃 / 紅 三段）：
- 每軸獨立設定 Warning / Alarm / Critical 門檻
- 觸發後寫入 `%LocalAppData%\Tranzx PHM\Alarms\`
- 同時推送到右側 WarningFeed 面板

**環境警告**：
- 溫度上下限（預設 25 / 35°C，可關閉）
- 濕度上下限（預設 55 / 65%，可關閉）

### 5.4 錄製功能（5 種 CSV 類型）

主視窗右下「錄製」按鈕可同時錄五類：

| 類型 | 內容 | 標準 header |
|---|---|---|
| **Vibration** | X/Y/Z Peak + RMS | `Time,X-peak,Y-peak,Z-peak,X-RMS,Y-RMS,Z-RMS` |
| **Tilt** | X/Y/Z 角度 | `Time,X-angle,Y-angle,Z-angle` |
| **Env** | 溫度 / 濕度 | `Time,Temperature,Humidity` |
| **Raw** | 原始振動 (高採樣率) | `Time,X,Y,Z` |
| **Stats** | 統計指標逐筆 | `Time,Min,Max,Mean,Median,StdDev,RMS,Peak,P-P,Crest` |

**錄製設定**（工具列 → 偏好設定 → 錄製設定）：
- 錄製模式：定時 / 持續 / 智慧
- 切檔週期（預設每小時切一檔）
- 各類型獨立 ON/OFF 開關

### 5.5 Smart Log 智慧錄製

**目的**：避免「閒置時錄一堆無聊數據」+ 「異常事件後才開始錄已經來不及」。

**運作機制**（state machine）：
1. **Idle**：振動 Peak max < trigger 門檻 → 不錄
2. **Triggered**：超過 trigger → 開始錄（含前 60 秒 buffer）
3. **Recording**：持續錄到振動回到 Idle 並維持 N 秒
4. **Cooldown**：等待下一次觸發

Smart Log 錄製專用資料夾：`%LocalAppData%\Tranzx PHM\Smart Log\`，強制 1 小時切檔。

### 5.6 Sensor Dashboard（多 Sensor 總覽）

工具列「☷」→ 開啟 Dashboard 視窗
- 每個 sensor 一張 380×360 卡片
- 顯示即時 X/Y/Z 警報狀態（綠 / 黃 / 紅）
- ICON 圖示快速辨識機台類型
- 點卡片回到該 sensor 的詳細頁

### 5.7 警報統計

工具列「📊」→ 警報統計視窗
- 列出每個 sensor 的 Warning / Alarm / Critical 觸發次數
- 時間區間篩選
- 警報事件 timeline

### 5.8 振動統計（趨勢圖表）

工具列「📈」→ 振動統計視窗
- 9 種統計指標即時計算（Min/Max/Mean/Median/StdDev/RMS/Peak/P-P/Crest）
- 可勾選哪些指標寫進 CSV
- 內嵌 PlotView 圖表 + XY 範圍可調

### 5.9 歷史分析

工具列「📂」→ 歷史分析視窗

**核心特色**（V1.0 強化）：
- **最多 4 組 CSV 疊加比較**
- **X 軸自動對齊**：以各 CSV 第一筆為 0 秒起點
- **手動對齊**：⚙按鈕 → 對話框輸入每組的對齊偏移量
- **滑鼠操作**：左鍵點擊 = 黑底白字 tracker、Ctrl+左鍵 = 框選縮放、右鍵拖曳 = 平移、滾輪 = 縮放
- **動態統計**：縮放範圍變動時即時重算 RMS / Peak / Crest 等
- **多檔比對**：自動標記✓最佳 / ✗最差 / ⚖最穩定
- **報告匯出**：PDF / HTML / CSV

**Y 軸自動切換**：依檔案類型 (Tilt → Angle°、Env → Temperature/Humidity、Vib → G)。

### 5.10 3D 動態回放（含 AVI / MP4 影片匯出）

工具列「🧊」→ 3D 動態回放視窗

**功能**：
- 載入 Tilt CSV（自動配對同時段 Vib CSV）
- 仿 iVS Sensor 黑色金屬盒 3D 模型，依 X-angle / Y-angle / Z-angle 即時旋轉
- 振動向量箭頭從盒子中心射出（依 |G| 切綠 / 黃 / 紅）
- 軌跡尾巴 + Lissajous (X-Y) 投影
- Lissajous 自動圖形辨識：圓形 / 橢圓 / 直線 / 8字 / 雜亂團
- 內建「向量箭頭 + 軌跡尾巴 + Lissajous」解析說明（可展開）
- 時間軸 scrubber + 播放速度 0.1×~10×

**🎬 AVI 匯出**：
- 工具列右上「🎬 匯出 AVI」按鈕
- 把整段 timeline 渲染成 1280×720 / 30fps 標準 AVI 影片
- 採用 Motion-JPEG 編碼（純 C# 實作，無外部依賴）
- 最長 60 秒（自動取樣）
- **建議用 VLC 播放**（https://www.videolan.org/vlc/）— Windows Media Player 對 MJPEG 解碼支援有限可能黑屏

**🎞 MP4 匯出（H.264）**：
- 工具列右上「🎞 匯出 MP4」按鈕
- 渲染流程：先在背景產暫存 AVI → 用 FFmpeg `libx264` 編成 H.264 MP4 → 自動清除暫存 AVI
- 編碼參數：CRF 23（畫質好且檔案小）+ fast preset + yuv420p（最高相容性）+ faststart（網頁播放友善）
- **任何播放器都能放**（WMP / VLC / 瀏覽器拖檔 / 手機）
- **需要系統有 ffmpeg.exe**

**FFmpeg 偵測順序**：
1. 應用程式同層的 `ffmpeg.exe`
2. 系統 PATH 中的 `ffmpeg.exe`
3. 常見路徑：`C:\ffmpeg\bin\`、`C:\Program Files\ffmpeg\bin\`、`C:\tools\ffmpeg\bin\`、`C:\ProgramData\chocolatey\bin\`
4. winget 安裝路徑（`%LocalAppData%\Microsoft\WinGet\Packages\Gyan.FFmpeg_*`）
5. Scoop 安裝路徑（`%UserProfile%\scoop\apps\ffmpeg\current\bin`）

**FFmpeg 安裝（三種方法擇一）**：
- 方法 1：到 https://github.com/BtbN/FFmpeg-Builds/releases 下載 `ffmpeg-master-latest-win64-lgpl.zip`，解壓後把 `bin\ffmpeg.exe` 放到本軟體資料夾或 `C:\ffmpeg\bin\`
- 方法 2（命令列）：`winget install Gyan.FFmpeg`
- 方法 3（命令列）：`choco install ffmpeg`

按「🎞 匯出 MP4」時若沒偵測到 ffmpeg，會跳對話框引導下載。如果不需要 MP4，用 AVI + VLC 即可（不需 FFmpeg）。

**3D 場景方位**：Z 軸朝上、XY 為水平面。Sensor 平放時 XYZ logo 朝上 +Z；Tilt 報告 X/Y 朝上時，盒子會自動側立顯示。

### 5.11 軟體狀況監控（Event Log）

工具列「⚠」→ Event Log 視窗
- 完整紀錄軟體 Info / Warn / Error
- 每日 `app_yyyyMMdd.csv` 切檔
- 雙 Feed 面板（連線狀態 + 警告分流）

### 5.12 偏好設定

工具列「⚙」→ Preferences
- 三語切換（zh-TW / en / ja）
- 節能模式（背景時降低 update rate）
- 警報門檻設定
- 環境警告 ON / OFF
- 圖表更新頻率
- 資料保留天數（自動刪除舊 CSV）

---

## 6. 資料夾結構

```
%LocalAppData%\Tranzx PHM\
├─ Trends\             ← 一般手動 / 定時錄製
│  └─ Sensor1\
│     ├─ Vibration\trend_Sensor1_yyyyMMdd_HHmmss_Vib.csv
│     ├─ Tilt\trend_Sensor1_yyyyMMdd_HHmmss_Tilt.csv
│     ├─ Env\trend_Sensor1_yyyyMMdd_HHmmss_Env.csv
│     └─ Raw\trend_Sensor1_yyyyMMdd_HHmmss_Raw.csv
├─ Smart Log\          ← Smart Log 智慧錄製專用（強制 1h 切檔）
├─ Stats\              ← 統計分析視窗的 CSV 輸出
├─ Alarms\             ← 警報事件記錄
└─ Logs\               ← 軟體 Event Log

%LocalAppData%\Tranzx.iVS4\
└─ settings.json       ← 全域設定（向後相容自動 migrate）
```

---

## 7. CSV 格式（含 metadata）

每個 CSV 前 8 行 metadata，第 9 行（空白）後才是 header：

```
Device ID:,Sensor1
Date:,2026/05/04
Start time:,21:30:15.123
Log Type:,Vib Trend
Range:,2G
Interval time(ms):,1000.0
Unit:,G
Application:,Tranzx iVS 4.0

Time,X-peak,Y-peak,Z-peak,X-RMS,Y-RMS,Z-RMS
2026/05/04 21:30:15.123,0.0234,0.0156,0.0421,0.0098,0.0072,0.0203
...
```

---

## 8. 鍵盤 / 滑鼠操作

| 動作 | 觸發 |
|---|---|
| 切換子模式 | 各 sensor 分頁上方按鈕 |
| 框選縮放（圖表）| Ctrl + 左鍵拖曳 |
| 平移（圖表）| 右鍵拖曳 |
| 縮放（圖表）| 滾輪 |
| 點選資料點查值 | 左鍵點擊 |
| 3D 視角旋轉 | 左鍵拖曳 |
| 3D 視角平移 | 右鍵拖曳 |
| 3D 視角縮放 | 滾輪 |
| 波形 P-P 量測 | Shift + 左鍵框選 |

---

## 9. 故障排除

| 症狀 | 對策 |
|---|---|
| Sensor 連不上 | 檢查 COM port、確認驅動安裝、拔插 USB 重試 |
| USB 拔除後再插，無法重連 | 等 30 秒讓 watchdog 偵測；不行則手動重連 |
| 圖表卡頓 | 偏好設定 → 開啟節能模式 |
| CSV 格式錯誤 | 檢查 Excel 是否誤改副檔名 |
| 警報亂響 | 偏好設定 → 警報門檻 → 校正各軸 baseline |
| 3D 視窗載入後盒子方向不對 | 該 Tilt CSV 第一筆角度被視為 sensor 真實放置方向；如要強制平放，使用 V2.0 |
| AVI 影片播不出來 | 用 VLC 開（Windows Media Player 對某些 MJPEG 不友善）|

---

## 10. 後續升級

V1 → V2：增加 **Raw 振動分析** 視窗，含完整 FFT 頻譜、軸承故障特徵頻率（BPFO/BPFI/BSF/FTF）、Filtered Lissajous（轉子診斷）、PDF 報告等。

如有客戶機台需要做 root cause 分析、軸承壽命預警，建議升級 V2。

---

> Tranzx Advanced Technology Co., Ltd.
> 創智先進科技股份有限公司
