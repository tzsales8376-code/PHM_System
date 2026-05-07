# Tranzx iVS 4.0 多通道振動量測軟體

整合 **iVS Gen4 校正軟體 (TZ_ACC_Tester v1.5)** 與 **iVS 2.0 USB 多通道振動感測器軟體**
重新打造的 iVS 4.0 專用量測平台。

## 專案結構（5 專案 .NET 8 Solution）

```
Tranzx.iVS4.sln
├── src/
│   ├── Tranzx.iVS4.Core            ← 通訊協議、資料模型（純邏輯，無外部相依）
│   ├── Tranzx.iVS4.Calibration     ← .tzcal/.sr 載入、補償引擎、自動配對
│   ├── Tranzx.iVS4.Analysis        ← RingBuffer、FFT、振動統計、HPF
│   ├── Tranzx.iVS4.Communication   ← USB CDC + BLE Transport、多通道管理、時鐘同步
│   └── Tranzx.iVS4.App             ← WPF MVVM 主程式（OxyPlot + CommunityToolkit.Mvvm）
└── tasks/
    ├── todo.md                     ← 5 階段開發計畫
    ├── changelog.md                ← 版本紀錄
    └── lessons.md                  ← 從 v1.5 繼承的 7 條教訓
```

## 五大關鍵設計決策

| # | 決策 | 實作位置 |
|---|------|----------|
| 1 | 嚴格時鐘同步（4 組共同時鐘） | `Communication/Sync/TimeSyncService.cs` — 啟動同步 + 60s 重同步 |
| 2 | 校正檔自動配對（依 Sensor ID） | `Calibration/CalibrationStore.cs` — 預設掃 `Calibration/` 資料夾 |
| 3 | USB + BLE 雙模 | `Communication/Transport/ITransport.cs` 抽象介面（BLE 留 Phase 4） |
| 4 | WPF MVVM 與 V2.0 一致 | `App.xaml` 深色主題色票對齊 Condition Analyzer V2.0 |
| 5 | Phase 1：1~2 通道驗證 | `MultiSensorManager` 4 通道槽位，可漸進啟用 |

## 編譯與執行

```powershell
cd Tranzx.iVS4
dotnet restore
dotnet build -c Release
dotnet run --project src\Tranzx.iVS4.App
```

**需求**：.NET 8 SDK、Windows 10 19041+（System.Management/SerialPort）

## Phase 1 操作流程（建議實機驗證步驟）

1. 將 1~2 個 iVS Sensor 透過 USB 接到電腦
2. 啟動程式 → 點選「🔍 掃描 USB」
3. 在通道矩陣中手動指派 COM Port 與 Sensor ID（目前需從 code 加，UI 對話框待 Phase 2 補完）
4. 點「▶ 連線啟動」→ 自動發送 SetFs/SetOdr → 啟動 stream
5. 切換到「📊 振動分析」Tab 看時域波形；切到「頻域 (FFT)」看 FFT
6. 切到「📐 水平量測」看 Pitch/Roll；「🌡 溫溼度」看 HDC1080 即時值與趨勢

## 校正檔自動配對

- 將 `.tzcal` 或 `.sr` 校正檔放到應用程式目錄下的 `Calibration\` 資料夾
- 命名建議：`TZ_CAL_<SensorID>.tzcal`（會自動依 Sensor ID 配對）
- 連線時若找到匹配的校正檔，通道矩陣會顯示「已校正 [SensorID]」
- 若未找到，仍可運作但無補償，UI 會顯示「未校正」

## Phase 2 / 3 / 4 待辦

詳見 `tasks/todo.md`，重點包括：
- BLE Transport 補完（`Windows.Devices.Bluetooth`）
- 多通道同步 CSV 錄製（含校正前/後雙欄）
- Smart USB Hub 整合 + auto-reconnect
- Recipe 管理
- Word 操作手冊 + Self-contained x64 部署

## 從 TZ_ACC_Tester v1.5 繼承的關鍵教訓

詳見 `tasks/lessons.md`，特別注意：
- **L01**：USB CDC `stop\r\n` 與設定指令間需 50 ms 延遲（已在 `SensorChannel.ApplyConfigAsync` 實作）
- **L02**：量程必須從設備讀取（建議 Phase 2 補上 ReadFs 確認流程）
- **L03**：FFT 軸用實測 SPS（`SpsSmoother.Current`），非設定 ODR
- **L04**：丟包追蹤用 SeqNo 跳號，不靠 Overflow 旗標
