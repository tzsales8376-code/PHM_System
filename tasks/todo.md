# Tranzx iVS 4.0 多通道振動量測軟體 — 開發 TODO

## 專案目標
整合 **iVS Gen4 校正軟體 (TZ_ACC_Tester v1.5)** 的硬體校正基本功能與
**iVS 2.0 USB 多通道振動感測器軟體** 的多通道架構，重新開發為 iVS 4.0 專用
量測軟體。

## 核心需求（已與業主確認）
- ✅ 多組量測：最大 4 組，動態啟用 1~4 組
- ✅ 採樣率：保持最高 (ODR 3332 Hz / 實測 SPS ~3600)
- ✅ 三大功能：振動 (時域+頻域)、水平、溫溼度
- ✅ 通道採樣：嚴格同步 — 4 組共同時鐘
- ✅ 校正檔：自動掃描 + 手動指定（預設自動）
- ✅ 連線：USB + BLE 雙模保留
- ✅ UI：WPF MVVM，與 Condition Analyzer V2.0 一致
- ✅ 第一階段：先 1~2 通道驗證，後擴 4 通道

## Solution 結構（5 專案）
- `Tranzx.iVS4.Core` — 通訊協議、資料模型（無外部相依）
- `Tranzx.iVS4.Communication` — USB CDC + BLE Transport、多通道管理
- `Tranzx.iVS4.Calibration` — .tzcal/.sr 載入、補償引擎
- `Tranzx.iVS4.Analysis` — RingBuffer、FFT、振動統計、傾角計算
- `Tranzx.iVS4.App` — WPF 主應用程式（MVVM）

## 開發階段

### Phase 1：基礎建設 (進行中)
- [x] Solution + 5 專案骨架
- [x] 移植 SensorProtocol.cs → `Tranzx.iVS4.Core.Protocol`
- [x] 移植 CalibrationData.cs → `Tranzx.iVS4.Calibration`
- [x] 抽象化 Transport 介面（USB / BLE 共用）
- [x] MultiSensorManager 多通道並行框架
- [x] 嚴格時鐘同步服務 TimeSyncService
- [x] tasks/ 三大文件（todo / changelog / lessons）

### Phase 2：通訊與校正
- [x] USB CDC Transport 實作（從 TZ_ACC_Tester 移植 SerialPort 邏輯）
- [ ] BLE Transport 實作（Phase 4 補完，目前留 stub，已抑制 CS0067）
- [x] USB 自動掃描 + Sensor ID 配對
- [x] 校正檔自動載入機制（CalibrationStore）
- [x] 通道級補償管線
- [x] **新增**：AddChannelDialog UI 對話框（取代 hard-coded AddUsbChannel）
- [x] **新增**：通道生命週期 UI（加入 / 移除 / 手動載入校正檔）
- [x] **新增**：CSV 同步錄製服務（CsvRecorder + Session 資料夾 + metadata header）
- [x] **新增**：連線狀態視覺化（每通道 State 指示燈）
- [x] **新增**：錄製進度顯示（pulse 動畫 + 樣本計數）
- [ ] ReadFs/ReadOdr 確認流程（Phase 3 補完，落實 L02 教訓）

### Phase 3：UI 三大功能
- [x] 主視窗：4 通道矩陣指示燈 + 功能 Tab
- [x] 振動分析 Tab：時域 OxyPlot 2×2 + FFT 切換
- [x] 水平量測 Tab：4 組儀表盤
- [x] 溫溼度 Tab：即時數字 + 趨勢圖
- [ ] Recipe 管理（Phase 5）

### Phase 3：資料完整性與工作流程
- [x] **新增**：ReadFs/ReadOdr 驗證流程（落實 L02 — 連線後讀回設備設定，UI 顯示警告）
- [x] **新增**：`ResponseParser` 解析二進位 + ASCII 雙格式設備回應
- [x] **新增**：`SensorChannel.RawMode` 切換機制（暫停 Parser.Feed 讀取命令回應）
- [x] **新增**：`ApplyConfigAsync` 整合 SetFs / SetOdr / 驗證於同一個 stream-stop 視窗
- [x] **新增**：Recipe 管理（`.tzrcp` JSON 檔，跨機器移轉設定）
- [x] **新增**：通道 Verification 標籤 + 全域 Mismatch 警告橫幅
- [ ] Session Inspector（離線 CSV 檢視，留 Phase 4）
- [ ] 離線 FFT 批次分析（留 Phase 4）

### Phase 4：多語系與 Recipe 自動配對
- [x] **i18n 基礎建設**：`LocalizationService` 單例 + DynamicResource 切換機制
- [x] 三份 `Strings.{lang}.xaml` 資源字典（zh-TW、en、ja，共約 80 keys）
- [x] App.xaml 載入預設字典；App.xaml.cs 啟動偵測系統語系自動切換
- [x] 全部主視窗、子 Tab、Dialog 改用 `{DynamicResource ...}`
- [x] OxyPlot 軸標題與 Series 名稱透過 `LanguageChanged` 事件動態重套
- [x] `TransportStateToTextConverter` 改用 Resource lookup
- [x] `ChannelViewModel`：訂閱 `LanguageChanged` 重算 Verification / Calibration 標籤
- [x] `MainViewModel`：`SetStatus(key, args)` helper，語系變更自動重算 StatusText
- [x] 工具列加入語系下拉選單（🇹🇼 / 🇺🇸 / 🇯🇵）
- [x] **Recipe SensorID 自動配對 USB 裝置**（落實 L13）
- [ ] Smart USB Hub 整合（依 Sam 指示延後到 Phase 5）
- [ ] BLE Transport 完整實作（延後 Phase 5）
- [ ] Session Inspector / 離線 FFT（延後 Phase 5）

### Phase 5：穩定化與離線分析
- [ ] 多通道同步 CSV 錄製（含校正前/後雙欄）
- [ ] Smart USB Hub 整合（自動重連 + 電源監控）
- [ ] BLE Transport 補完

### Phase 5：操作手冊與部署
- [ ] Word 操作手冊
- [ ] Self-contained x64 發佈
- [ ] 簽章與安裝程式

## 關鍵設計決策

### 嚴格時鐘同步策略
1. 連線時 `TimeSyncService` 對所有通道發送 `CMD A2 (Set Time)` 帶相同 PC 時間戳
2. 每包封包帶設備時間戳 (sec + ms)，PC 端用設備時間做對齊
3. 每 60 秒重新同步一次以補償時鐘漂移
4. FFT 與跨通道分析以「設備時間戳對齊後的視窗」為基準

### 校正檔自動配對
- 預設掃描資料夾：`Calibration/` (App 目錄下)
- 連線後讀取 Sensor ID → 在資料夾搜 `TZ_CAL_<ID>.tzcal` 或 `.sr`
- 找不到時 UI 顯示警告並提供手動載入按鈕
- 補償套用點：`PacketParser` 之後、`RingBuffer` 之前

### 採樣率保持最高
- ODR 預設 3332 Hz，連線時自動下發
- 實測 SPS 由 `SpsSmoother` 動態計算（過去韌體 ODR 設定不一定生效，但 SPS 量測準確）
- FFT 頻率軸用實測 SPS，不用設定 ODR

## 待釐清事項
- BLE 在 4 通道並行下的封包延遲與穩定性需實機驗證
- 嚴格同步的精度需求？目前設計可達 ±5 ms 對齊精度
- 4 通道全速錄 CSV 時的磁碟 I/O 是否需要批次寫入

## 風險與緩解
| 風險 | 影響 | 緩解 |
|---|---|---|
| 4 通道同時 ODR=3332 時 USB 頻寬不足 | 封包遺失 | Phase 1 用 1~2 通道驗證頻寬 |
| BLE 4 通道並行延遲過大 | 同步失敗 | BLE 模式限制最多 2 通道 |
| .tzcal 校正檔遺失 | 無補償運作 | 提供無校正模式 + UI 警告 |
| 設備 ODR 設定不生效 | 採樣率不穩 | 採用實測 SPS 計算 FFT 軸 |
