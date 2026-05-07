# Tranzx iVS 4.0 — Changelog

## 2026-05-03 (Phase 5-2.2) — 三軸角度雙模式（水平儀 / 向量夾角）
- 新增 `TiltAngleMode` enum，Preferences 對話框內可切換：
  - **`Inclinometer`（水平儀模式，預設）**：
    - 公式：`90 - acos(Ai / |g|)`
    - 平放時 X=0°、Y=0°、Z=+90°（Z 朝上垂直）
    - 範圍 -90° ~ +90°
    - 工程界水平儀慣例（VMS V2.0 / 客戶現場習慣）
  - **`GravityVector`（向量夾角模式）**：
    - 公式：`acos(Ai / |g|)`
    - 平放時 X=90°、Y=90°、Z=0°（軸正向與重力夾角）
    - 範圍 0° ~ 180°
    - 物理嚴謹定義
- Y 軸範圍依模式自動切換：
  - 水平儀模式 → -90° ~ +90°
  - 向量夾角模式 → 0° ~ 180°
  - 歸零後一律 ±TiltYRangeDeg
- 切換 TiltAngleMode 時：
  - ChannelViewModel: 強制 LPF 重新 prime + 取消歸零（offset 在不同模式下意義不同）
  - SensorTabViewModel: 清空水平 sweep + 重套 Y 軸
- 三語新增 5 keys：
  - `Pref.TiltMode.Label / Inclinometer / InclinometerTip / GravityVector / GravityVectorTip`
- 三語系合計 239 keys 對齊

## 2026-05-03 (Phase 5-2.1) — 三軸對重力夾角 + DC 算傾角 + LPF + 歸零 toggle
- **取消 Pitch / Roll / Total**，改用「三軸對重力夾角」（定義 A）：
  - `AngleX = acos(Ax / |g|)`，靜置平放 ≈ 90°；垂直立起（X 朝上）≈ 0°
  - `AngleY = acos(Ay / |g|)`
  - `AngleZ = acos(Az / |g|)`，靜置平放 ≈ 0°
  - 自由落體 / 加速規異常時 |g|<0.05 不更新
- **DC 算傾角** + **1-pole LPF**：
  - DC = `VibrationStats.Compute(samples).Mean`（已平均掉振動）
  - LPF 公式：`y[k] = α·x[k] + (1-α)·y[k-1]`，α = 1 - exp(-Δt/τ)
  - Δt = 1/StatsHz；τ 預設 1.0 秒，可選 0.2/0.5/1.0/2.0/5.0
  - 機台振動環境下水平讀數明顯更穩
- **Preferences 加水平角度 GroupBox**：
  - ☑ 啟用 LPF（預設 true）
  - 時間常數下拉（依 LPF 啟用狀態 enable/disable）
- **歸零按鈕變 toggle**：
  - 第一次按 → 把當前 X/Y/Z LPF 後角度當成 offset，IsTiltZeroed = true
  - 第二次按 → offset 清零，恢復絕對角度
  - 按鈕外觀 Style.Triggers：
    - 未歸零：琥珀底色 + 「歸零」文字
    - 已歸零：Teal 底色 + 「取消歸零」文字
- **Y 軸範圍依歸零狀態切換**：
  - 未歸零：絕對角度 0~180°
  - 歸零後：相對偏移 ±TiltYRangeDeg（預設 ±90°）
- **水平 series CheckBox** 從 Pitch/Roll/Total 改為 `█ X° / █ Y° / █ Z°`
- 新增三語 keys：
  - `Tilt.ZeroReset`（取消歸零）
  - `Status.TiltZeroResetFmt`
  - `Pref.Section.Tilt / Pref.TiltLpf.Enable / Tip / Tau`
- AppSettingsService 新增：`TiltLpfEnabled`、`TiltLpfSec`、`TiltLpfChanged` 事件
- ChannelViewModel 移除 Pitch/Roll/TotalTilt 屬性，新增 AngleX/Y/Z + IsTiltZeroed
- 三語系合計 234 keys 對齊

## 2026-05-03 (Phase 5-2) — Statistics Rate / Overlap + 三模式並行 sweep + ChartSettings + 水平歸零
- **時域統計改用獨立計時器**（與圖表更新解耦）：
  - 新增 `AppSettingsService.StatisticsHz`（預設 20Hz，可選 1/2/5/10/20/40/50/100/200）
  - 新增 `StatisticsOverlapPct`（預設 30%，可選 0/10/25/30/50/75）
  - `ChannelViewModel` 改成兩個 DispatcherTimer：
    1. **Stats timer**（依 StatisticsHz）→ 從 ring buffer 取 N 樣本
       (N = sps × (1/Hz) × (1+overlap)) 算 Peak/RMS/Crest/Tilt → 更新即時值表
    2. **Diag timer** 500ms → bytes / packets / sps 顯示
  - 圖表畫線速度 (33ms) 與統計頻率完全分離，圖表流暢度不受影響
- **三模式同時累積**（Q3 第一項）：
  - SensorTabViewModel 三套獨立 sweep 狀態：`_vibT/_tiltT/_envT`
  - OnRefresh tick 三模式都更新（連線後即開始累積，無論當前看哪一個）
  - 切換 ViewMode 不再 reset 資料，只切 IsVisible 與軸範圍
  - 切到溫溼度後立刻看到從連線 0 秒開始的歷史
- **ChartSettings 對話框**（圖表上方獨立 ⚙ icon）：
  - 振動 X 軸：5/10/20/60/120 秒；Y 軸：Auto/±0.5/±1/±2/±5/±16 G
  - 水平 X 軸：30/60/120/300 秒；Y 軸：±10/±30/±90/±180°
  - 溫溼度 X 軸：60/300/600/1800/3600 秒；Y 軸：可自訂 Min~Max（預設 0~100）
  - 任一變更觸發 `ChartSettingsChanged` 事件 → 所有 SensorTabViewModel 重套軸並 reset
- **TimeDomainSettings 對話框**（圖表上方獨立 ⏱ icon）：
  - Statistics Rate / Overlap 兩個下拉
  - 變更觸發 `StatisticsSettingsChanged` 事件 → 所有 ChannelViewModel 改 stats timer interval
- **水平歸零按鈕**（水平模式才顯示）：
  - 圖表上方 📐 歸零 按鈕
  - 按下：`ChannelViewModel.TiltZeroNow()` 把當前 Pitch/Roll 累加到 offset，
    後續顯示為相對於該姿態的偏移；同時清空水平 sweep 從 0 重畫
- **圖表上方標籤**（Sensor 編號 + 名稱 + Port）：
  - 例：`● Sensor 1 — 通道 1 (COM7)`
  - 圖表 icon bar 最左側，色點 + 文字，方便切 Tab 時辨識
- 三語新增 22 keys：`ChartSettings.* / TimeDomain.* / Tilt.Zero / Status.TiltZeroedFmt`
- 三語系合計 228 keys 對齊

## 2026-05-03 (Phase 5-1.4) — 重力扣除選項 + Preferences 對話框
- **修 8 個 CS8826 警告**：CommunityToolkit.Mvvm source generator 產生的 partial method
  簽章用 `bool value`，我之前寫的是 `bool v`，Visual Studio 警告。全部改為 `value`。
- **重力處理選項**（依需求預設扣除）：
  - 新增 `GravityMode` enum：`RemoveGravity`（預設）/ `KeepGravity`
  - `AppSettingsService.GravityMode` 屬性 + `GravityModeChanged` 事件
  - SensorTabViewModel.UpdateVibrationSweep 套用：
    - RemoveGravity → 用 batch 的 mean 當 DC 估計，每軸減去 DC 後再取絕對值
    - KeepGravity → 直接取絕對值（保留 1G）
  - 切換時 SensorTabView 訂閱 `GravityModeChanged` 自動清空 sweep 重畫
- **新增 Preferences 對話框**（依需求軟體最上面 ⚙ 設定 icon）：
  - `Views/PreferencesDialog.xaml(.cs)` 集中設定面板
  - 三區塊：振動顯示（GravityMode RadioButton）/ 顯示（FontScale ComboBox）/ 未來預留
  - 工具列右側「⚙ 設定」按鈕觸發開啟（`MainViewModel.OpenPreferencesCommand`）
  - 字型下拉從工具列搬入 Preferences，工具列更乾淨
- 新增 12 條三語 keys：
  - `Toolbar.Preferences / Pref.Title / Pref.Apply`
  - `Pref.Section.Vibration / Display / Future / Pref.Future.Hint`
  - `Pref.Gravity.Label / Remove / RemoveTip / Keep / KeepTip`
- 三語系合計 210 keys 對齊

## 2026-05-03 (Phase 5-1.3) — 各 Sensor 獨立圖表 + 1-based 編號 + 取絕對值
- **核心架構修正**：PlotModel + sweep 狀態從 SensorTabView 搬到 SensorTabViewModel
  - 之前：TabControl 切 Tab 時 View 重建，sweep 跟著歸零
  - 現在：每個 Sensor 持有自己的 PlotModel，DispatcherTimer 在 ViewModel 持續跑，
    切 Tab 看到的是該 Sensor 一直累積的歷史
  - SensorTabView 變得很薄（只負責 RadioButton 與 CheckBox 群顯示切換）
  - SensorTabViewModel 實作 IDisposable，Detach 時停 timer
- **時域振動取絕對值**：
  - X/Y/Z series title 改 `|X| / |Y| / |Z|`
  - Y 軸標題 `Acceleration |G|`
  - Y 軸下限 0（不再 -1G ~ +1G），靜置時 Z≈+1、X/Y≈0 看起來統一
- **Sensor 編號 1~4**：
  - AddSensorDialog 的 Slot ComboBox 用 ChannelIndexPlus1Converter 顯示 1-based
  - 預設名稱 "通道 N" 改 "Sensor N"（三語）
  - "Dialog.SlotIndex" / "Toolbar.ActiveChannels" 字串改用「Sensor」
- **取消 Max 紀錄**：
  - 即時值表第 5 欄 (Max) 整欄拿掉，剩 4 欄 (Label / Acc / Alarm / Status)
  - 拿掉「Max 重置」按鈕
  - ChannelViewModel 移除 12 個 Max* 屬性 + ResetMax() 方法
  - OnPacket 不再追蹤歷史 Max
- **CheckBox 改雙向 binding**：
  - `ShowX/Y/Z`、`ShowPitch/Roll/Total`、`ShowTemp/Hum` 改為 ViewModel 的
    [ObservableProperty]，CheckBox.IsChecked 直接 TwoWay binding
  - View 不再需要 OnSeriesToggleChanged 處理器
- **AddSensorDialog 細節**：Slot ComboBox 加 ItemTemplate，用 Converter 顯示 1-based

## 2026-05-03 (Phase 5-1.2) — 三模式統一 sweep + 自訂 Legend
- **三模式行為統一為 sweep**（依使用者選項 A）：
  - 振動：20 sec 視窗 / ±16G / 預設 ±1G
  - 水平：60 sec 視窗 / -180~180° / 預設 -90~90
  - 溫溼度：600 sec 視窗 / **Y 軸固定 0~100**
  - 都用同一份 `_sweepLocalT` 計時，左→右畫到底 clear 重畫
  - 取消舊的「rolling 60s window」邏輯
- **CheckBox legend 群**（依使用者選項：圖表上方 icon bar 旁邊）：
  - 振動模式 → ☑ X / ☑ Y / ☑ Z（紅/綠/藍）
  - 水平模式 → ☑ Pitch / ☑ Roll / ☑ Total（紅/綠/琥珀）
  - 溫溼度 → ☑ 溫度 / ☑ 濕度（紅/藍虛線）
  - CheckBox 內用 `█ X` 色塊代替方塊圖示，文字字體放大到 RootFontSize（之前 OxyPlot
    內建 Legend 是 10pt 太小）
  - 切換 ViewMode 時自動切換 CheckBox 群的 Visibility（一次只看到當前模式的）
- **OxyPlot 內建 Legend 拿掉**（自訂 CheckBox 已替代）
- **Y 軸標題加單位**：
  - 振動：`Acceleration (G)`
  - 水平：`Angle (°)`
  - 溫溼度：`Temp (°C) / Humidity (%)`
- **PlotMargins 微調**：左 58 / 下 36，刻度與單位字較大也不擠
- 新增三語 keys：`Series.Temp / Series.Hum`

## 2026-05-03 (Phase 5-1.1) — ViewMode icon 化 + 連線即繪圖
- **工具列 View 下拉拿掉**，改在每個 Sensor Tab 的圖表上方放三個 RadioButton icon：
  - 📊 振動 / 📐 水平 / 🌡 溫溼度
  - 點任一 icon → 透過 `AppSettingsService.ViewMode` **全域同步切換**
    所有 Sensor Tab 的圖表內容（依使用者選項 A）
  - 外部 ViewMode 變更（其他 Tab、程式啟動）會反向同步 RadioButton checked 狀態，
    用 `_suppressRadioEvent` 避免 setter 與事件互相觸發無限迴圈
- **連線後自動繪圖**（依使用者選項 3）：
  - 移除「IsMeasuring」要求；連線完成 `IsPausedPlot=false` 圖表立即動
  - ▶/⏸ 按鈕語意改為「暫停/恢復**繪圖**」，資料持續接收
  - 同步控制 `SyncStartAll/SyncStopAll` 改為操作 IsPausedPlot
- **新增**：
  - `App.xaml` 加 `ModeIconButton` Style — RadioButton 看起來像 Tab/Pill：
    未選 = 暗色背景，選中 = 亮色背景 + 底部 AccentBlue 線條
  - 三語 ViewMode 標籤：`ViewMode.Vibration / Tilt / Env`
- **移除**：
  - `MainViewModel.CurrentViewMode / ViewModeOptions` 屬性（不再有控制點）
  - `MainWindow.xaml` 的 ViewModeValues ObjectDataProvider 與 ComboBox
  - 工具列 `Toolbar.View / Toolbar.View.Tip` 仍保留 key（暫不刪，避免破壞別處引用）

## 2026-05-03 (Phase 5-1) — VMS 風格版面骨架重做
- **大幅版面重設計**（依 Sam 指示，向客戶熟悉的 iVS 2.0 / VMS V3.0 看齊）
- 採用 **Tab-based 多 Sensor 結構**：
  - 每個 Sensor 一個獨立 Tab（取代原本的「通道矩陣」並排）
  - Tab Header 顯示通道色標 + 編號 + 連線狀態 LED
- **單列工具列**（VMS 風格）：
  - `[+ 加入Sensor] [− 移除] [作用通道:N/4]` ─ Sensor 管理
  - `[▶ 全部開始] [■ 全部停止]` ─ 同步控制
  - `[● 全部錄製 / ■ 停止錄製]` ─ 同步錄製（按鈕用 Style Trigger 切換顏色與文字）
  - 中段：錄製狀態指示 (REC + 時間 + 樣本數)
  - 右側：`[View ▼] [Font ▼] [語系 ▼]`
- **全域 ViewMode 切換**（依 Sam 規格）：
  - `AppSettingsService.ViewMode` enum (`Vibration / Tilt / Env`)
  - 工具列下拉一鍵影響**所有 Sensor Tab** 的圖表內容
  - 透過 `ViewModeChanged` 事件，每個 SensorTabView 訂閱後重置 series
- **每個 Sensor Tab** 採 VMS 三區塊版面：
  - 左欄 (220px MinWidth 180)：GroupBox 控制群（連線/量測/設定/Log/校正）+ 診斷
  - 右上：6×5 即時值表格（X-0P / Y-0P / Z-0P / X-Rms / Y-Rms / Z-Rms × Acc/Alarm/Status/Max）
  - 右下：OxyPlot 圖表（依 ViewMode 顯示對應 series）
- **解析度 / 字型優化**：
  - 加入 `app.manifest` 宣告 `PerMonitorV2` DPI 感知
  - App.xaml 預先註冊 `RootFontSize / LargeFontSize / SmallFontSize / TinyFontSize / DataValueFontSize`
  - 工具列 ComboBox 提供 Small / Normal / Large / X-Large 字型大小選擇
  - 全部 XAML 移除絕對 px，使用 `*` / `Auto` / `MinWidth/MinHeight`
- **新增**：
  - `Services/AppSettingsService.cs`（單例，ViewMode + FontScale）
  - `ViewModels/SensorTabViewModel.cs`（每 Sensor 自己的 connect/start/stop/record）
  - `ViewModels/AddSensorDialogViewModel.cs`
  - `Views/SensorTabView.xaml(.cs)`
  - `Views/AddSensorDialog.xaml(.cs)`
  - `Converters/CommonConverters.cs`（Bool/Visibility/Equals 共用）
- **移除**：
  - `Views/AddChannelDialog`、`Views/LogSettingsDialog`、`Views/VibrationTabView`、
    `Views/LevelTabView`、`Views/EnvTabView`（功能融入 SensorTabView）
  - `ViewModels/LogSettingsDialogViewModel`、`ViewModels/AddChannelDialogViewModel`
  - `Converters/ChannelLayoutConverters`（通道矩陣 Converter 不再需要）
  - 所有 Recipe 相關 UI / Command（依 Sam 指示「先取消」）
- **重寫**：
  - `MainWindow.xaml` 全新工具列 + TabControl
  - `MainViewModel`：Tab 集合管理、全域 ViewMode binding、同步錄製命令
  - `ChannelViewModel`：加上歷史 Max 紀錄（Peak/Rms × XYZ × 時間戳）+ Crest factor
  - `App.xaml` 加入 FontSize 動態資源、Style 統一套用
- 新增三語 Resource keys（共 +25 keys，三語同步達 194 條）：
  - `Toolbar.AddSensor / RemoveSensor / SyncLabel / StartAll / StopAll / RecordAll / View / FontSize`
  - `Section.Connection / Measure / Settings / Log / Calibration`
  - `Channel.Connect / Disconnect / RecordStart / RecordStop / OpenFolder / MaxReset / PortLabel`
  - `Table.Acc / Alarm / Status / MaxRecord`
  - `Tab.Sensor / Tab.Stats`
  - `Status.SensorConnectedFmt / SensorDisconnectedFmt / SyncStartedFmt / SyncStopped`
  - `Dialog.AddSensor.Title`

## 2026-04-28 (Phase 4 — Log Settings)
- 新增「⚙ Log 設定」對話框與工具列按鈕
  - 列出所有作用中通道，每個一個 CheckBox（預設全勾）
  - 「全選」/「全不選」快速按鈕
  - 底部即時顯示「已選通道數」
- 行為：
  - 點「● 開始錄製」依設定選擇的通道錄製，預設**所有作用中通道同步錄製**
  - 使用者可在 Log 設定中減少要錄的通道（例如只錄 Ch1+Ch3）
  - 設定保留在記憶體中（`MainViewModel._logSelection`），不寫進 Recipe
  - `_logSelection = null` 表示「跟隨作用通道全錄」（預設）
  - `_logSelection = HashSet<int>` 表示「只錄選中的 index」
- 新增檔案：
  - `Views/LogSettingsDialog.xaml` + `.cs`
  - `ViewModels/LogSettingsDialogViewModel.cs`（含 `ChannelLogItem` 巢狀類）
  - `Converters/ChannelLayoutConverters.cs` 加入 `ChannelIndexPlus1Converter`
- 三語新增 10 keys：`Toolbar.LogSettings` / `Log.SettingsTitle` / `SelectAll` /
  `SelectNone` / `Confirm` / `SelectedCount` / `NoChannelsSelected` /
  `SelectionHintFmt` / `SettingsHint` / `Tip`

## 2026-04-28 (Phase 4 — 圖表規格 v2)
- **三 Mode 切換**：時域 (Sweep) / FFT / **Waveform (新)**
- **FFT 改進**：
  - Y 軸 default `MinimumRange = 0.5G`（從 1G 改）
  - 加入「✦ 最大振幅標記」：ScatterSeries 黃星標 + TextAnnotation 文字
    （`X 最大: f=120.5Hz · A=0.1234G`），跳過 DC bin (0Hz)
  - 只標**啟用軸**中的最大者（受 X/Y/Z CheckBox 影響）
  - **支援滑鼠圈選 zoom**（左鍵拖曳）+ 雙擊 reset + wheel zoom
- **Waveform 新功能**：
  - 「📷 截圖」按鈕凍結當下 RingBuffer 全部資料（最多 8192 樣本）
  - 完整波形展開可細看
  - 同 FFT 支援 box zoom + cursor
- **時域 sweep 仍禁用滑鼠 zoom**（避免 sweep 排版錯亂）
- **XYZ 三軸 CheckBox**（控制列頂端，預設都勾）：
  - 取消勾選對應軸隱藏該 LineSeries
  - 影響 FFT max marker 的計算（只看啟用軸）
- **統計卡新增 Crest factor**：
  - Crest = Peak / RMS（純正弦≈1.41，>3 表衝擊性振動）
  - 在 ChannelViewModel.OnPacket 計算
  - UI 顯示在 RMS / P-P 之下
- **「🔄 重設縮放」按鈕**：手動 reset 所有軸範圍
- **PlotController 雙模式**：
  - `_noZoomController`：時域 sweep 用，左鍵 click → cursor，其他禁用
  - `_zoomController`：FFT / Waveform 用，左鍵拖曳 → box zoom，雙擊 → reset，
    右鍵點 → cursor，wheel → zoom

## 2026-04-28 (Phase 4 — NRE 修正)
- 修正 `tbCursorInfo` 為 null 的 `NullReferenceException`
- XAML 改用後置 element 的 `x:Name="brdCursorInfo"`，移除前向 ElementName binding
- 新增 `SetCursorInfo(text)` helper 含 null guard
- 抑制 `SensorChannel.OnConfigVerified` 的 CS0067 警告（保留供 Phase 5）
- 新增 lessons L18 — XAML 前向 ElementName binding 陷阱

## 2026-04-28 (Phase 4 — 圖表行為規格)
- **時域 X 軸**：固定寬度，使用者選 5/10/20/60/300/600/1200 秒，預設 20s
- **Sweep 模式**：sweep 走到視窗底自動清空從左重畫（不是 sliding）
- **時域 Y 軸**：AutoFit 但限制 ±16G，預設視窗 ±1G (`MinimumRange=2`)
- **FFT X 軸**：預設 Auto (Nyquist=sps/2)，可選 100/500/1000/1666 Hz
- **FFT Y 軸**：AutoFit ≥0
- **禁用滑鼠 zoom/pan**（避免排版錯亂）：
  - `axis.IsZoomEnabled = false`、`axis.IsPanEnabled = false`
  - `PlotController.UnbindAll()` 解除所有預設 binding
  - 套用對象：振動 4 圖 + 環境趨勢圖
- **點擊 Cursor**：
  - LeftClick → 顯示垂直黃色虛線 + 底部資訊列
  - 振動：`t={t:F3}s · X={x:F3}G · Y={y:F3}G · Z={z:F3}G`
  - FFT：`f={f:F1}Hz · X={x:F4}G · Y={y:F4}G · Z={z:F4}G`
  - 控制列加「⊗ 清除游標」按鈕
- **長視窗 Downsample**：
  - 目標每張圖最多 8000 點
  - Stride 自動計算：1200 秒 × 3332sps → stride=500（每 500 筆取 1）
  - 短視窗 (5~20s) stride=1 完整保留
- 新增 11 個 resource keys（三語）：`Vibration.WindowLengthLabel` /
  `FreqRangeLabel` / `SecUnit` / `HzUnit` / `AutoFreq` / `CursorFmt` /
  `CursorFftFmt` / `ClearCursor`

## 2026-04-28 (Phase 4 — 連線修正 + 移除 BLE)
- **關鍵 Bug 修復**：`SensorChannel.ApplyConfigAsync` 改為 **No-op**
  - 原因：v1.5 校正工具能連同款韌體，代表韌體預設就會自動串流 241B/0x45 封包；
    我之前送的 `stop\r\n` → SetFs → SetOdr → `start\r\n` 反而把韌體推進「raw 6-byte BE」
    奇怪狀態，PacketParser 永遠找不到 `0x45` header（症狀：Bytes=113K 持續累積、
    ValidPackets=0、圖表只有殘影）
  - 現在連線後僅設定 `ScaleFactor`，相信韌體預設行為（落實 L17）
- **移除 BLE 功能**（依 Sam 指示，先完整 USB 模式）：
  - 刪除 `Tranzx.iVS4.Communication/Transport/BleTransport.cs`
  - `MultiSensorManager.Attach` 移除 `TransportType.Ble` 分支
  - `AddChannelDialog.xaml` 連線設定改為單一「🔌 USB CDC」標籤（不再是 RadioButton）
  - 三語 `Dialog.Ble` resource key 移除
  - `AddChannelDialogViewModel.TransportMode` 改為 readonly 永遠 USB
- **UI 微調**：
  - 通道卡片移除 `VerificationLabel` 行（不再驗證）
  - 改顯示 `Bytes: N | Pkt: M` + 最後 16 byte hex preview（保留診斷用）
  - 連線狀態訊息簡化為 `已連線 N 個通道` (`Status.ConnectedFmt`)
  - 不再顯示 Mismatch 警告橫幅（HasMismatchWarning 永遠為 false）
- 新增 lessons L17 — 不要假設韌體需要 wake-up；尊重設備預設

## 2026-04-28 (Phase 4 — 連線診斷)
- 新增 Raw byte 診斷顯示（通道卡片底部）：
  - `Bytes: N` 累計收到的位元組數
  - 後接最後 16 byte 的 hex preview（hover 看完整 ToolTip）
- `SensorChannel` 新增 `RawBytesReceived` (Interlocked) 與 `GetLastBytesHex(n)`
- `ChannelViewModel` 新增 500ms `DispatcherTimer` 持續 pull 診斷資料
  （即使 Parser 沒收到合格封包也會更新，方便排查連線問題）
- 用途：判讀「連線了但 SPS=0」屬於哪一種：
  - Bytes=0 → 設備完全沒傳資料（韌體未喚醒 / start 指令沒效）
  - Bytes 持續增加但 ValidPackets=0 → 格式對不上（baudrate / 封包協議）
  - Bytes 增加且 ValidPackets 也增加 → 解析正常

## 2026-04-28 (Phase 4 — UI 微調)
- **動態通道版面**：
  - 軟體開啟時無通道，顯示「🔌 請按 + 加入通道」空狀態提示
  - 加入通道後版面隨數量分割：1→滿版、2→左右、3→三等分、4→2x2
  - 影響範圍：MainWindow 通道矩陣、VibrationTabView 圖表 + 統計卡、
    LevelTabView、EnvTabView 即時數值卡
- 新增 Converters：
  - `ChannelCountToColumnsConverter` (1→1, 2→2, 3→3, 4→2)
  - `ChannelCountToRowsConverter` (1~3→1, 4→2)
  - `IndexVisibleByCountConverter` 用 ConverterParameter 控制每個 PlotView
    的可見性（pv1: count≥1, pv2: count≥2, …）
  - `NoChannelsToVisibilityConverter` (count==0 → Visible)
- 全部 `<UniformGrid Columns="4">` / `<UniformGrid Columns="2" Rows="2">`
  替換為 `RelativeSource AncestorType=Window` 綁定到 `ActiveChannelCount`
- VibrationTabView 4 個 PlotView 透過 IndexVisibleByCount 動態顯示，
  不必動 code-behind 邏輯（PlotModel 仍預先建立，未顯示時不參與排版）

## 2026-04-28 (Phase 4)
- **Phase 4 啟動**：多語系（i18n）+ Recipe SensorID 自動配對。Smart USB Hub 與
  BLE 完整實作依 Sam 指示延後 Phase 5。
- 新增 `Tranzx.iVS4.App.Services.LocalizationService` 單例：
  - 透過動態交換 `Application.Current.Resources.MergedDictionaries` 實現執行期語系切換
  - 提供 `Get(key)` / `Format(key, args)` API 給 ViewModel 使用
  - `LanguageChanged` 事件供子元件重算動態字串
  - `DetectSystemLanguage()` 從 `CultureInfo.CurrentUICulture` 自動偵測
- 新增三份語系資源字典（約 80 keys/語系）：
  - `Resources/Strings.zh-TW.xaml`（預設）
  - `Resources/Strings.en.xaml`
  - `Resources/Strings.ja.xaml`
- 全部 XAML 改用 `{DynamicResource ...}`：
  - `MainWindow.xaml`、`VibrationTabView.xaml`、`LevelTabView.xaml`
  - `EnvTabView.xaml`、`AddChannelDialog.xaml`
- `MainWindow.xaml` 工具列右側加入 ComboBox 語系選擇器（🇹🇼/🇺🇸/🇯🇵）
- `App.xaml` MergedDictionaries 預設載入 `Strings.zh-TW.xaml`
- `App.xaml.cs` `OnStartup` 自動偵測系統語系並切換
- `MainViewModel`：
  - 加入 `SetStatus(key, args)` / `RefreshStatus()` helper（落實 L15）
  - 訂閱 `LanguageChanged` 自動重算 StatusText
  - 公開 `Localization` 屬性供 XAML binding 使用
  - 全部錯誤 MessageBox / 狀態訊息改透過 `Loc[key]` / `Loc.Format()`
  - `BuildRecipeSummary()` 取代原本 `RecipeFile.Summary()` 的硬編碼字串
- `ChannelViewModel`：
  - 訂閱 `LanguageChanged`，重算 CalibrationLabel / VerificationLabel
  - 強制 `OnPropertyChanged(State)` 觸發 Converter 重新計算 State 文字（落實 L14）
- `TransportStateToTextConverter` 改用 `TryFindResource` 做 key lookup
- 新增 `StringNotEmptyToVisibilityConverter` — Recipe 名為空時隱藏顯示列
- OxyPlot 圖表（Vibration / Env）的 Axis Title 與 Series Title：
  - 在 `LanguageChanged` 觸發時用 `Application.Current.Dispatcher.BeginInvoke` 重設
  - `EnvTabView` Series 用 `Env.SeriesTempFmt` / `Env.SeriesHumFmt` placeholder
- **Recipe SensorID 自動配對 USB 裝置**：
  - `MainViewModel.ResolveConfig()`：載入 Recipe 時若原 PortName 不存在於系統，
    自動從 VID 命中的 iVS 候選 Port 依槽位 Index 分配（落實 L13 的後續處理）
- 字串 Format 約定：含 placeholder 的 key 後綴 `Fmt`（例如 `Status.ConnectingFmt`），
  方便辨識與避免 String.Format 錯誤套用（落實 L15）

## 2026-04-28 (Phase 3)
- **Phase 3 啟動**：資料完整性與工作流程
- 新增 `Tranzx.iVS4.Core.Protocol.ResponseParser`：解析設備命令回應
  - 同時支援二進位格式 `[0xCC, CMD, LEN, PARAM, CRC]` 與 ASCII `"FS=16"` / `"ODR=3332"`
  - 大端/小端兩種 byte order 都試（韌體尚未確認）
  - 解析失敗時回傳 null，不阻塞流程
- 重構 `SensorChannel`：
  - 新增 `RawMode` 機制：讀取命令回應期間暫停 `Parser.Feed`
  - `ApplyConfigAsync` 整合 SetFs / SetOdr / Verify 於**同一個 stop-start 視窗**內完成
  - 新增 `DeviceFs` / `DeviceOdr` / `ConfigMismatch` 屬性紀錄驗證結果（落實 L02）
  - 新增 `OnConfigVerified` 事件供 UI 訂閱
- 新增 `Tranzx.iVS4.Core.Models.RecipeFile`：
  - `.tzrcp` JSON 格式打包多通道設定
  - 含 metadata（建立者、時間、AppVersion）
  - `Save()` / `Load()` / `Summary()` API
- `MainViewModel` 新增 `SaveRecipeCommand` / `LoadRecipeCommand`
  - 載入時顯示摘要 → 確認 → 自動 Attach 所有通道
  - 跨機器載入時 PortName 不存在會在連線時失敗，使用者需手動修正
- `ChannelViewModel` 新增 `DeviceFs` / `DeviceOdr` / `ConfigMismatch` / `VerificationLabel` 屬性
- 主視窗新增：
  - 「💾 儲存 Recipe」「📂 載入 Recipe」工具列按鈕
  - 全域 Mismatch 警告橫幅（任一通道驗證不一致時顯示）
  - 通道卡片底部 Verification 標籤
  - 標題列顯示當前 Recipe 名稱

## 2026-04-28 (Phase 2)
- **Phase 2 啟動**：可實機操作的關鍵 UX 與功能
- 修復 CS0067 警告：`BleTransport.OnDataReceived` Phase 4 stub 加 `#pragma warning disable CS0067`
- 新增 `AddChannelDialog`（XAML + ViewModel + code-behind）：完整連線設定 UI，取代 hard-coded `AddUsbChannel`
  - USB 裝置自動掃描 + ComboBox 選擇（VID 命中者排前）
  - Sensor ID、FS、ODR 完整可選
  - 校正檔自動 / 手動雙模式
- 新增 `CsvRecorder` Service：
  - 多通道同步錄製，每通道獨立 CSV
  - Session 資料夾命名：`Records/Run_<yyyyMMdd_HHmmss>/`
  - Metadata header (#) 含 Sensor ID/FS/ODR/Calibration 資訊
  - BufferedStream 高頻寫入
  - Session metadata.txt 紀錄全 Run 資訊
- 主視窗新增「+ 加入通道」、「● 開始錄製 / ■ 停止錄製」、錄製進度 pulse 指示燈
- 通道矩陣每張卡新增 State 指示燈（依 TransportState 變色）+「📁 載入校正檔」+「✕ 移除」按鈕
- 4 個新 Converter：`InverseBool`、`StateToColor`、`StateToText`、`BoolToVis` / `InverseBoolToVis`
- ChannelViewModel 訂閱 `OnCalibrationChanged` 事件，校正檔變更時即時更新 UI

## 2026-04-28 (Phase 1)
- Phase 1 啟動：5 專案 Solution 骨架建立完成
- 移植 `SensorProtocol.cs` (TZ_ACC_Tester v1.5) → `Tranzx.iVS4.Core.Protocol`，重構為 PacketParser + CommandBuilder 兩個獨立類別
- 移植 `CalibrationData.cs` → `Tranzx.iVS4.Calibration`，新增 `CalibrationStore` 自動掃描配對
- 新增 `ITransport` 抽象介面，USB CDC 為首版實作，BLE Transport 留 stub
- 新增 `MultiSensorManager` 4 通道並行框架，每通道獨立執行緒
- 新增 `TimeSyncService` 嚴格時鐘同步（啟動同步 + 每 60s 重同步）
- WPF MVVM 主視窗：4 通道矩陣 + 三大功能 Tab (Vibration / Level / Env)
- tasks/ 三大文件依標準六原則建立

## 2026-05-03 (Phase 5-2.3) — 圖表效能調優
- **症狀**：使用者反映運行頓
- **根因分析**：
  - 圖表用 30Hz refresh（每 33ms invalidate 一次）
  - 振動圖每 series 4000 點 × 3 series = 12000 點
  - OxyPlot 在 WPF 是 CPU 渲染（沒 GPU 加速）
  - 30 × 12000 = 每秒 360,000 個點要轉成像素 → 主瓶頸
  - 多 Sensor 累乘
- **優化**（純調參數，不影響功能）：
  1. `RefreshIntervalMs` 33ms (30Hz) → **67ms (15Hz)**，CPU 砍半
  2. `TargetMaxPointsPerSeries` 4000 → **1500**，渲染快 2.6 倍
  3. UpdateVibrationSweep 加 `anyAdded` 旗標，stride filter 全拒時跳過 InvalidatePlot
- **預期效果**：振動模式下 CPU 占用降至原先 ~25%（2x refresh + 2.6x points）
- **答覆使用者問題**「圖表用 SPS 3300 還是 20Hz 跑？」：
  - SPS 3332 = 韌體進 ring buffer 速率（不變）
  - StatsHz 20Hz = 即時值表 / AI 特徵更新速率（Preferences 可調）
  - 圖表 refresh = **獨立的 30Hz**（現改 15Hz），跟前兩者都無關

## 2026-05-03 (Phase 5-2.4) — 效能調校放進 Preferences + 背景 Tab 停 sweep
- **背景 Sensor Tab 不再跑 sweep**：之前所有 SensorTabViewModel 不管前景背景都在 OnRefresh
  跑 UpdateVibrationSweep / UpdateTiltSweep / UpdateEnvSweep。改成只有 IsActiveTab=true
  的才跑。MainViewModel.OnSelectedSensorTabChanged 切換時設定。
  - 切回 Tab 時 OnIsActiveTabChanged → ResetSweepForCurrentMode 從 0 重新累積
  - 2 個 Sensor 時，UI thread 負荷砍半
- **效能參數放進 Preferences**：
  - `ChartRefreshHz`：圖表更新率 (Hz)，預設 10Hz，可選 5/8/10/15/20/30
  - `ChartMaxPoints`：每軸最大點數，預設 1000，可選 500/800/1000/1500/2000
  - 變更觸發 PerformanceSettingsChanged 事件 → SensorTabViewModel 重設 timer interval + 清空 sweep
  - 使用者可在現場依機器效能微調，不影響量測精度（韌體 sps 與 stats hz 都不變）
- **預設值再降一階**：refresh 15Hz → 10Hz、點數 1500 → 1000
- 三語新增 4 keys：`Pref.Section.Performance / Perf.Hint / RefreshHz / MaxPoints`
- 三語系合計 243 keys 對齊

## 2026-05-03 (Phase 5-2.5) — DispatcherPriority + OxyPlot.SkiaSharp + Decimator
- **症狀**：12700K + RTX3080 跑 2 個 sensor 仍卡頓，「圖表一禎一禎貼」+「點 icon 反應慢」
- **根因（最關鍵發現）**：所有資料更新 BeginInvoke 用預設 `DispatcherPriority.Normal`，
  跟 input event 同優先級且通常排前面。每秒 50+ 個資料更新塞爆 dispatcher queue，
  使用者點 icon 要排隊在所有資料更新之後 → 即使 12700K 也救不了
- **三刀同下**：
  1. **DispatcherPriority.Background**：高頻資料更新（stats + vib sweep）的 BeginInvoke
     改用 Background priority。Input/Render 永遠優先，使用者操作立即響應
  2. **OxyPlot.Wpf → OxyPlot.SkiaSharp.Wpf 2.2.0**：API 完全相容，只換 NuGet + xaml namespace
     - `xmlns:oxy="http://oxyplot.org/wpf"` → `http://oxyplot.org/skiawpf`
     - SkiaSharp 走 GPU/CPU SIMD 加速，渲染快數倍
  3. **LineSeries 三項加速**：
     - `Decimator = OxyPlot.Series.Decimator.Decimate`（振動三軸）：
       畫面寬度像素只保留 min/max 2 點，視覺等效但點數大幅減少
     - `EdgeRenderingMode = PreferSpeed`：抗鋸齒關閉
     - 兩者結合，即使 1000 點 series 也只實際畫 ~50 個 line segments
- **預期**：12700K 上 CPU 占用 < 5%，UI 完全無感

## 2026-05-03 (Phase 5-3) — UI 簡化：拔功能、量測 toggle、即時值表跟著 ViewMode
- **量測按鈕簡化為單一 toggle**：
  - 量測中 → 綠燈 + ⏸ + 「量測中」（按下進入暫停）
  - 已停止 → 紅燈 + ▶ + 「已停止」（按下恢復量測）
  - 用 Style.Triggers 切換 Background / Text，新增 ToggleMeasureCommand
- **拔掉左側不必要功能**：
  - HPF (0.5Hz) checkbox 整個「設定」GroupBox 拔除
  - 「校正」GroupBox 拔除（瀏覽 + 校正狀態文字）
  - FS=±G16 ODR=Hz3332 顯示也一併移除（屬於設定區塊）
- **診斷面板搬到 Preferences 控制**：
  - AppSettingsService 新增 `ShowDiagnostics` 屬性，預設 false
  - 左側 Bytes/Pkt/Lost/Hex preview Border + 量測區的 SPS 顯示
    都綁 `AppSettings.ShowDiagnostics`（用 BoolToVis converter）
  - Preferences → 效能調校區塊底部加「☐ 顯示診斷面板」checkbox
- **即時值表跟 ViewMode 同步**：
  - SensorTabViewModel 新增 IsVibrationMode / IsTiltMode / IsEnvMode 三旗標
  - OnGlobalViewModeChanged 自動更新
  - SensorTabView.xaml 即時值表內三個 Grid 用 Visibility 切換：
    - 振動：6 列（X-0P / Y-0P / Z-0P / X-Rms / Y-Rms / Z-Rms）—【顯示 Acc(G)】
    - 水平：3 列（X° / Y° / Z° 三軸對重力夾角）—【顯示 Angle(°)】
    - 溫溼度：2 列（溫度 °C / 濕度 %）—【顯示 Value】
  - 切換時數據與單位都同步變化
- **新增三語 keys**：Table.Angle / Table.Value / Measure.Running / Measure.Stopped /
  Pref.Diag.Show / Pref.Diag.Tip / Table.Alarm 改為通用「Alarm」（不再寫死 G）
- 三語系合計 249 keys 對齊

## 2026-05-03 (Phase 5-4) — SCADA 圓環 + Box Zoom + Alarm 閾值對話框
- **連線按鈕簡化為單一 toggle**（仿量測按鈕）：
  - 未連線 → 紅燈 + 🔌 + 「連線」
  - 已連線 → 綠燈 + ✓ + 「斷線」
  - 拿掉原本的雙按鈕 + 狀態 LED
- **OxyPlot 互動**：
  - 軸 IsZoomEnabled / IsPanEnabled 設為 true
  - 自訂 PlotController：左鍵框選 = box zoom，右鍵拖曳 = pan，滾輪 = zoom
  - PlotView 綁定 ChartController
  - 兩個新 icon：
    - 🔍 還原縮放（ResetZoomCommand → ApplyAxesForMode 重套軸）
    - 🔃 重新刷新（RefreshChartCommand → 清空 sweep 從 0 重畫）
- **Alarm 三段閾值模型**（Models/AlarmModels.cs）：
  - `AlarmLevel` enum (Green / Yellow / Red)
  - `AlarmThreshold` (Yellow / Red 兩個閾值，取絕對值比較)
  - `ChannelAlarmThresholds` 含 11 個量值的閾值（XPeak/YPeak/ZPeak/XRms/YRms/ZRms/AngleX/Y/Z/Temp/Hum）
  - 每個量值獨立、預設值依量值類型不同：
    - 振動 0-P：黃 0.3 / 紅 0.5 G
    - 振動 RMS：黃 0.1 / 紅 0.2 G
    - 角度：黃 5° / 紅 10°
    - 溫度：黃 40 / 紅 60 °C
    - 濕度：黃 70 / 紅 85 %
- **AlarmLevelToBrushConverter** (MultiValueConverter)：
  - (value, yellow, red) → SolidColorBrush
  - 綠 #1ABC9C / 黃 #F39C12 / 紅 #E74C3C
- **即時值表 → SCADA 圓環**：
  - 振動模式：3 個圓環（X / Y / Z），上 0-P 大字 + LED、下 RMS 大字 + LED
  - 水平模式：3 個圓環（X°/Y°/Z°），單一角度顯示
  - 溫溼度模式：2 個圓環（溫度 °C / 濕度 %）
  - 圓環外框 BorderBrush 隨 0-P alarm 等級變色（綠/黃/紅）
  - LED 圓點隨對應量值 alarm 等級即時變色
  - 軸名稱用 32px 大字 + 通道色（紅/綠/藍）
  - 整個量值區塊是個 Button，點擊開啟 AlarmThresholdDialog
- **AlarmThresholdDialog**：
  - 顯示目標量值名稱（Sensor + 軸 + 量值）
  - 三段閾值編輯：綠（自動 0 ~ 黃）/ 黃（可調）/ 紅（可調）
  - 即時預覽：「綠：0~0.30 G / 黃：0.30~0.50 G / 紅：>0.50 G」
  - **「複製到群組」按鈕**：依 key 自動辨識：
    - X/Y/Z Peak → 複製到所有軸 0-P
    - X/Y/Z Rms → 複製到所有軸 RMS
    - X/Y/Z Angle → 複製到所有軸角度
  - 驗證：紅必須大於黃、兩者皆 ≥ 0
- **三語新增 17 keys**：Chart.ResetZoom / Chart.Refresh / Alarm.* (15 個)
- 三語系合計 266 keys 對齊
- **下一刀（Phase 5-5）將做**：
  - AlarmLogger 服務（CSV 寫入：每 Sensor 每天一個檔）
  - 左側面板 Alarm GroupBox（資料夾路徑 + 今日警報計數 + 開資料夾 icon）
  - 黃/紅燈轉變記錄（含綠燈解除以便算 Alarm 持續時間）

## 2026-05-03 (Phase 5-5) — SCADA 半圓弧儀表 + Tooltip 格式 + ChartSettings bug fix
- **Bug fix：ChartSettings 套用後 X/Y 軸不變**：
  - 根因：使用者 box-zoom 後，OxyPlot 軸的 ActualMin/Max 被改寫
    ApplyAxesForMode 設定 Minimum/Maximum 但沒重置 ActualMin/Max
  - 解法：ApplyAxesForMode 開頭呼叫 `_xAxis.Reset()` + `_yAxis.Reset()`
- **Bug fix：Tooltip 小數位過多**：
  - 根因：OxyPlot 預設 TrackerFormatString 不格式化數字
  - 解法：每個 LineSeries 個別設定 TrackerFormatString：
    - 振動：`"{0}\nTime: {2:F2} s\nAcc: {4:F3} G"` (時間 F2、Acc F3)
    - 角度：`"{0}\nTime: {2:F2} s\nAngle: {4:F2}°"` (F2)
    - 溫度：`"Temp\nTime: {2:F2} s\n{4:F1} °C"` (F1)
    - 濕度：`"Hum\nTime: {2:F2} s\n{4:F0} %"` (F0)
- **GaugeArc UserControl**（SCADA 半圓弧儀表）：
  - 240×160 Canvas + Viewbox 自動 scale
  - 弧範圍 240°（從 8 點鐘經 12 點鐘到 4 點鐘，centerY=100, radius=70）
  - 三段背景顏色：綠（0~Yellow）/ 黃（Yellow~Red）/ 紅（Red~Max=Red×1.2）
  - 動態繪製：依 Yellow/Red 閾值用 ArcSegment 計算每段 PathGeometry
  - 中央指針（白色 Line + 圓底座）依 Value 旋轉
  - 中央大字數值 + 子標籤（"0-P" / "RMS" / "Angle" / "Temperature" / "Humidity"）
  - DependencyProperty: Value / Yellow / Red / ValueFormat / Unit / SubLabel
- **SensorTabView 替換圓環為 GaugeArc**：
  - 振動模式：3 個 axis panel（X/Y/Z），每 panel 內
    - 上方軸名（X/Y/Z 32px 大字 + 通道色）
    - 中間 GaugeArc 顯示 0-P 弧形儀表（點擊開 alarm dialog）
    - 下方 RMS sub-display：LED + 數字（獨立 alarm dialog）
  - 水平模式：3 個 GaugeArc 顯示 X/Y/Z 角度
  - 溫溼度模式：2 個 GaugeArc 顯示溫度（°C, F1）/ 濕度（%, F0）
  - 每個 GaugeArc 與 RMS sub 都包在 Button 內，點擊開 AlarmThresholdDialog
- **下一刀（Phase 5-6）將做**：
  - AlarmLogger 服務（CSV 寫入：每 Sensor 每天一個檔，含黃/紅/綠燈轉變含解除以便算 Alarm 持續時間）
  - 左側 Alarm GroupBox（資料夾路徑選擇 + 今日警報計數綠/黃/紅 + 開資料夾 icon）

## 2026-05-03 (Phase 5-6) — AlarmLogger CSV 紀錄 + 左側 Alarm 面板
- **AlarmLogger 服務**（Services/AlarmLogger.cs，singleton + ObservableObject）：
  - 偵測 alarm level 轉變（Green ↔ Yellow ↔ Red 互相切換）
  - 寫入 CSV：每 Sensor 每天一個檔
    `alarm_{SensorName}_{yyyy-MM-dd}.csv`
  - **包含綠燈解除**（從黃/紅回到綠也記錄），方便算 Alarm 持續時間
  - CSV 欄位：Timestamp, Sensor, Key, FromLevel, ToLevel, Value, Yellow, Red
  - UTF-8 BOM 編碼（Excel 讀繁體中文不亂碼）
  - 第一次出現的 (Sensor, Key) 不寫 CSV（避免啟動時誤報所有量值都「轉變」到 Green）
  - Thread-safe（lock + dispatcher.BeginInvoke 切到 UI thread 更新計數）
  - 寫檔失敗不會 crash（try/catch + Debug.WriteLine）
- **今日警報計數**（給 UI 綁定）：
  - `TodayGreenCount` / `TodayYellowCount` / `TodayRedCount`
  - 跨日自動重置（每次寫檔時檢查 DateTime.Today 是否變了）
- **AppSettingsService 加 AlarmLogFolder**：
  - 預設 `%LocalAppData%\Tranzx.iVS4\Alarms`
  - 寫成 INotifyPropertyChanged，UI 即時更新
- **ChannelViewModel 整合**：
  - stats 計算 callback 末端呼叫
    `AlarmLogger.Instance.CheckChannel(Index, DisplayName, Thresholds, values)`
  - values dictionary 含 11 個量值：XPeak/YPeak/ZPeak/XRms/YRms/ZRms/AngleX/Y/Z/Temp/Hum
- **左側 Alarm GroupBox**（位於 Log 之後、診斷面板之前）：
  - 路徑顯示（綁 AppSettings.AlarmLogFolder）
  - **🔍 瀏覽**按鈕（Microsoft.Win32.OpenFolderDialog，.NET 8 內建）
  - **📂 開資料夾** icon 按鈕（System.Diagnostics.Process.Start）
  - **今日計數三段** Border：綠/黃/紅 各自顯示計數
- **SensorTabViewModel 新增 commands**：
  - `BrowseAlarmFolderCommand` — 選資料夾
  - `OpenAlarmFolderCommand` — 用檔案總管打開
  - `AlarmLogger` 屬性 → 暴露 singleton 給 XAML 綁定計數
- **三語新增 3 keys**：Section.Alarm / Alarm.LogFolder / Alarm.TodayCount
- 三語系合計 269 keys 對齊

## 2026-05-03 (Phase 5-7a) — TrendLogger CSV + Raw Data + Alarm Sound + 設定持久化
- **5-6 補丁：Alarm CSV 加 metadata header**：
  - 仿 VMS2.0 trend csv 格式，第一次寫入時加 metadata 區
    `Device ID:,Sensor1` `Date:,2026/05/03` `Start time:,...` `Log Type:,Alarm Log`
- **TrendLogger 服務**（Services/TrendLogger.cs）：
  - 仿 VMS2.0 trend csv 格式 metadata header
    （Device ID, Date, Start time, Log Type, Range, Freq Range, Interval time(ms), Unit）
  - 三模式 + Raw 共四種紀錄類型：
    - Vibration: `Time, X-peak, Y-peak, Z-peak, X-RMS, Y-RMS, Z-RMS`（依 StatisticsHz 寫）
    - Tilt: `Time, X-angle, Y-angle, Z-angle`
    - Env: `Time, Temperature, Humidity`
    - VibrationRaw: `Time, X, Y, Z`（每個 sample 一行，~3300 sps）
  - 檔名 `trend_{SensorName}_{yyyyMMdd_HHmmss}_{Mode}.csv`
  - ConcurrentDictionary 管理多 Sensor recorder（thread-safe）
  - StreamWriter Flush + Dispose 在 Stop 時確保資料落地
- **ChannelViewModel 整合**：
  - `OnIsRecordingChanged` 啟動 / 停止 logger
  - 鎖定錄製模式（依當下 ViewMode + RawDataEnabled）
  - stats 計算 callback 內依 mode 寫 trend
  - `OnPacket` 內若 raw mode 寫每個 sample（X_G/Y_G/Z_G）
- **左側 Log GroupBox 擴充**：
  - Trend 紀錄資料夾（路徑 + 瀏覽 + 📂 開資料夾）
  - **Raw Data 全紀錄** checkbox（錄製中無法切換）
  - 開始/停止錄製按鈕（按一次切換）
- **AppSettingsService 新增屬性 + JSON 持久化**：
  - `TrendLogFolder` 預設 `%LocalAppData%\Tranzx.iVS4\Trends`
  - `RawDataEnabled` 預設 false
  - `AlarmSoundEnabled` 預設 true
  - `Save()` / `Load()`：`%LocalAppData%\Tranzx.iVS4\settings.json`
  - 持久化欄位：AlarmLogFolder, TrendLogFolder, RawDataEnabled, AlarmSoundEnabled, ShowDiagnostics
  - App.OnStartup 開機 Load；每個 setter 改變時自動 Save
- **Alarm 音效**：
  - 黃燈 → `SystemSounds.Asterisk.Play()`
  - 紅燈 → `SystemSounds.Hand.Play()`
  - 綠燈解除不播
  - 綁 `AppSettingsService.AlarmSoundEnabled`，Preferences 內可開關
- **Preferences 新增 Alarm Sound checkbox**
- **App.OnExit 呼叫 TrendLogger.Instance.StopAll()**：確保關閉前 flush
- **三語新增 5 keys**：Trend.LogFolder / Trend.RawData / Trend.RawDataTip /
  Pref.AlarmSound.Show / Pref.AlarmSound.Tip
- 三語系合計 274 keys 對齊
- **下一刀（Phase 5-7b）將做**：
  - FFT / Waveform sub-mode（在振動 mode 下加子頁籤）
  - Windows Toast 通知
- **再下一刀（Phase 5-7c）將做**：
  - 多 Sensor Dashboard
  - 歷史警報統計畫面（讀 alarm csv → 統計 → 圖表）

## 2026-05-03 (Phase 5-7b) — Trend 並行 3 CSV + 自動切割 + 自動清理 + 修 5-7a Bug
- **Bug fix (CS1503)**：ChannelViewModel.cs:96/97
  - 根因：5-7a 用 string 解析 `Channel.Config.FullScale` / `Odr`，但這兩個是 enum
    （`FullScale.G16` / `OutputDataRate.Hz3332`）
  - 解法：直接 `(int)enum`，enum 值就是數字（G16=16、Hz3332=3332）
  - 移除 `ParseFullScaleG` / `ParseOdrHz` 兩個無用 helper
- **Alarm 音效預設改為「關閉」**（依使用者要求）：
  - `AppSettings._alarmSoundEnabled = false`
  - SettingsSnapshot record 預設值同步改 false
  - Preferences 對話框可勾選啟用
- **CSV header 拿掉 Freq Range**（依使用者要求）：
  - 保留 Range / Interval time(ms) / Unit
  - 加 Application: Tranzx iVS 4.0
- **TrendLogger 重構：每 Sensor 子資料夾 + 並行 3+1 CSV**：
  - 目錄結構：`Trends/Sensor1/trend_..._Vib.csv` ＋ `..._Tilt.csv` ＋ `..._Env.csv`（並行寫入）
    啟用 Raw 時加上 `..._Raw.csv`
  - 開始錄製 → 一次開三個 trend writer（Vib / Tilt / Env）+ 可選 Raw
  - 移除 `TrendLogMode` / `_activeRecordMode`（不再依當前 ViewMode 決定，永遠寫三個）
  - SegmentState 結構：每個 csv 獨立的 writer + lock + segment 起始時間
- **CSV 自動切割**（每 N 分鐘換新檔）：
  - `TrendSegmentMinutes`（5/10/30/60/120/360，預設 60）
  - `RawSegmentMinutes`（同樣選項，獨立設定）
  - 寫入前檢查 `(Now - SegmentStart) > segmentMinutes` → 關舊檔開新檔
  - 檔名包含時間戳 → 自然排序
- **CsvRetentionService 自動清理**：
  - `TrendRetentionDays`（7/30/60/90/180/365，預設 90）
  - `RawRetentionDays`（同樣選項，獨立設定）
  - App 啟動時呼叫 `CleanupAll()`，並啟動 24 小時 DispatcherTimer
  - **FIFO + 一次只刪 7 天份**：找出最舊的過期檔，刪除「最舊那一天起 7 天內」的檔，避免 IO 衝擊
  - 依檔名後綴判斷 trend / raw（_Raw.csv vs 其他）→ 各自套用對應保留天數
- **RecordingSettingsDialog 對話框**：
  - 紀錄資料夾（路徑顯示 + 瀏覽 + 開資料夾 icon）
  - Trend 區塊：CSV 切割時間 + 保留天數
  - Raw 區塊：CSV 切割時間 + 保留天數
  - **「立即清理」按鈕**：用當前選擇執行 `ManualCleanup`，回傳已刪除檔案數
- **左側 Log GroupBox 簡化**：
  - 拿掉舊的 path 顯示 + 瀏覽按鈕（移到設定對話框）
  - Raw Data toggle 保留（最常切換的選項）
  - 開始/停止錄製按鈕
  - 底部兩個並排 icon 按鈕：⚙ 設定 / 📂 開資料夾
- **AppSettingsService 持久化擴充**：
  - 加 4 個欄位：TrendSegmentMinutes / TrendRetentionDays / RawSegmentMinutes / RawRetentionDays
  - SettingsSnapshot record 同步擴充
  - SegmentMinutesOptions / RetentionDaysOptions 公開供對話框使用
- **三語新增 12 keys**：Recording.Title / Recording.Settings / Recording.TrendSection /
  Recording.TrendDesc / Recording.RawSection / Recording.RawDesc / Recording.SegmentTime /
  Recording.Retention / Recording.Min / Recording.Day / Recording.CleanNow / Recording.CleanedFmt
- 三語系合計 286 keys 對齊

## 2026-05-03 (Phase 5-7c) — 振動趨勢圖 6 條 stats trend + 錄製範圍/類型擴充
- **5-7b RecordingSettingsDialog crash 修復**：
  - 根因：`<Run Text="{DynamicResource Recording.Min}"/>` 寫在 DataTemplate 內
    Run 是 Inline 不是 FrameworkElement，被 ComboBox 動態實例化時 inheritance context
    跟 LogicalTree 連結不可靠，DynamicResource 解析失敗 → 構造 dialog 時 throw
  - 解法：拔掉 DataTemplate，改用 code-behind 包裝 `DurationItem` record
    `override ToString() => $"{Value} {Suffix}"`，本地化字串只在 dialog 構造時取一次
  - lessons.md L25 已記錄
- **振動趨勢圖改 6 條 stats trend**：
  - 舊：每個 sample 算 abs 寫入 _sx/_sy/_sz（即時瞬時值，跟圓環邏輯重複）
  - 新：每軸 2 條 series — 0-P 實線 1.5px + RMS 虛線 1.2px
    - X-0P / Y-0P / Z-0P（Title="X-0P" 等，TrackerFormatString="{0:l} 0-P\nTime: {2} s\n{4} G"）
    - X-RMS / Y-RMS / Z-RMS（LineStyle.Dash）
  - 訂閱 `Channel.PropertyChanged`，`PropertyName == nameof(PeakX)` 時 push 一個時間點到 6 條
    （PeakX/Y/Z + RmsX/Y/Z 都是同個 stats tick 計算出來）
  - sweep clear 邏輯：`_vibT >= window` → Points.Clear() 6 條 + reset t=0
  - X 軸時間步進：`dt = 1/StatisticsHz`（預設 20Hz → dt=50ms）
  - 移除 UpdateVibrationSweep 內 ring buffer snapshot 取瞬時值的整段
    （改用 stats，跟圓環一致）
  - X/Y/Z 三個 checkbox 同時控對應軸的 0-P + RMS（一鍵兩條）
  - ResetSweepForCurrentMode 加清 RMS 三條
- **錄製設定擴充：紀錄範圍 + 紀錄類型**：
  - `LogScopeAll` (default true)：true = 點任一 Sensor 開始錄 → 全部 Sensor 一起錄；
    false = 只錄該 Sensor
  - `LogVibration` / `LogTilt` / `LogEnv` (default all true)：可勾選哪幾種 trend csv 開啟
  - `MainViewModel.ToggleSensorRecording` 重寫：依 LogScopeAll 走 ToggleSyncRecording 或個別切
  - `TrendLogger.StartRecording` 依 settings 旗標決定要開哪些 writer
    全 false 時回 null（視為失敗）
  - `WriteLine` 加 null guard — 沒開的 writer 不寫
- **Raw Data toggle 從左側移到設定對話框**：
  - 左側 Log GroupBox 變更為：[開始/停止錄製] + [⚙ 設定] [📂 開資料夾]
  - RecordingSettingsDialog 新增「紀錄範圍/紀錄類型」GroupBox 在最上方
    含 cbScopeAll、cbLogVib、cbLogTilt、cbLogEnv、cbLogRaw 五個 checkbox
- **AppSettingsService 持久化擴充 4 個欄位**：
  - SettingsSnapshot record 加 LogScopeAll/LogVibration/LogTilt/LogEnv
  - Save / Load 同步處理
- **三語新增 4 keys**：Recording.ScopeSection / Recording.ScopeAll /
  Recording.ScopeAllTip / Recording.LogTypes
- 三語系合計 290 keys 對齊
- **下一刀（Phase 5-8）**：FFT / Waveform sub-mode、多 Sensor Dashboard、
  歷史警報統計畫面、Windows Toast 通知

## 2026-05-03 (Phase 5-8) — 振動 sub-mode：Trend / Waveform / FFT 三子頁籤
- **新 enum `VibrationSubMode`**：Trend (預設) / Waveform / Fft
- **AppSettings.VibrationSubMode** 屬性 + `VibrationSubModeChanged` 事件
- **SensorTabViewModel 新增 6 條 series**：
  - `_wavX/_wavY/_wavZ`：原始時域波形（不取 abs，可正可負，1px 細線）
  - `_fftX/_fftY/_fftZ`：FFT 單邊振幅譜（1.2px）
- **ApplySeriesVisibility 重寫**：依 ViewMode + SubMode 決定哪 6 條（trend）/ 3 條（waveform）/ 3 條（fft）顯示
- **ApplyAxesForMode 振動 case 分三子情況**：
  - Trend：X=Time(s), Y=Acc|G| (0~max)（5-7c 既有）
  - Waveform：X=Time(s), Y=Acc(G) (±max，可顯示負值)
  - Fft：X=Frequency(Hz, 0~Nyquist), Y=Amplitude(G, auto)
- **OnRefresh 重構**：依當前 ViewMode + SubMode 只跑該組 update
  - Vibration+Trend：不在這裡跑（PropertyChanged 觸發）
  - Vibration+Waveform：UpdateWaveform()
  - Vibration+Fft：UpdateFft()
  - Tilt：UpdateTiltSweep()
  - Env：UpdateEnvSweep()
- **UpdateWaveform**：
  - 從 ring buffer 取最近 VibXSec 秒 × sps 個 sample
  - 去 DC（依 GravityMode）
  - stride 縮減到 ChartMaxPoints
  - 整段 Clear + AddRange 重畫（不做 sweep）
- **UpdateFft**：
  - 取最近 4096 samples → ComputeAmplitudeSpectrum (Hanning 窗，CG 校正)
  - 去 DC 避免 0Hz 尖峰遮蔽
  - 跳過 k=0 bin
  - stride 縮減到 ChartMaxPoints
- **UI 振動 mode 旁加三個 sub-mode RadioButton**：
  - 📈 趨勢 / 〰 波形 / 🔊 頻譜
  - 只在 IsVibrationMode=true 時顯示（Visibility binding）
  - GroupName="VSub" 確保互斥
  - 切換時自動同步所有 Sensor Tab（透過 ViewMode 同樣的 dispatch 機制）
- **三語新增 3 keys**：VibSub.Trend / VibSub.Waveform / VibSub.Fft
- 三語系合計 293 keys 對齊
- **下一刀（Phase 5-8b）將做**：多 Sensor Dashboard、歷史警報統計畫面、Windows Toast 通知

## 2026-05-03 (Phase 5-8b) — 振動量測設定 + Sub-mode dropdown + 修波形長度
- **Bug fix：波形只到 2 秒**：
  - 根因：SensorChannel 內 `RingBuffer(8192)` 容量 = ~2.46s @ 3332Hz
  - 解法：擴大到 `RingBuffer(204_800)` ≈ 60s @ 3332Hz（三軸 ~4.7MB / sensor）
- **Sub-mode UI 改成 dropdown**：
  - 移除原本同層三個 RadioButton（趨勢/波形/頻譜）
  - 振動 RadioButton 內顯示當前 sub-mode chip（teal 小標籤）
  - 旁邊加 ▾ 按鈕，按下開 ContextMenu 三選項（含 ✓ 標記）
  - 切換 ViewMode → 自動隱藏 ▾ 按鈕（只在振動 mode 顯示）
- **「⚙」icon 改為「振動量測設定」**（在振動模式下）：
  - OpenChartSettingsCommand 內依 ViewMode 開不同對話框
  - 振動 mode → VibrationMeasurementSettingsDialog（新）
  - 水平 / 溫溼度 → ChartSettingsDialog（既有）
  - Tooltip 同步：振動 mode 顯示「振動量測設定」，其他顯示「圖表設定」
- **VibrationMeasurementSettingsDialog 新對話框**：
  - **趨勢區**：X 軸時間（沿用 VibXSecOptions）、Y 軸最大（沿用 VibYMaxOptions）
  - **波形區**：顯示時間（1/2/5/10/20/30/60 秒，default 5）
  - **FFT 區**：
    - 頻率範圍上限（150/250/500/1000/1600 Hz，default 250）
    - 振幅軸上限（Auto / 0.01 / 0.05 / 0.1 / 0.5 / 1 / 5 / 10 G，default Auto）
    - 窗函數（Hanning / Hamming / Blackman / FlatTop / Rectangular，default Hanning）
    - Lines of Scan N（1024 / 2048 / 4096 / 8192，default 2048）
- **AppSettingsService 新增 5 個振動量測屬性**：
  - WaveformSec / FftFreqMax / FftYMax / FftWindow / FftN
  - 對應 *Options 公開陣列供對話框使用
  - 觸發 ChartSettingsChanged event（重套軸）
- **UpdateWaveform 依 WaveformSec 取整段**（受惠於增大的 ring buffer）
- **UpdateFft 用 Settings.FftN + Settings.FftWindow**（取代 hardcoded 4096 / Hanning）
- **ApplyAxesForMode**：
  - Waveform：X 軸 = WaveformSec
  - FFT：X 軸 = FftFreqMax，Y = FftYMax (>0) 或 Auto
- **三語新增 14 keys**：VibSub.MenuTip / VibSet.* (12) / Unit.Sec / Unit.Auto
- 三語系合計 307 keys 對齊
- **滑鼠圈選 P-P 計算**：工程量大（需要新 chart controller + RectangleAnnotation 互動），下一刀單獨處理
- **下一刀（Phase 5-8c）**：滑鼠圈選 P-P + 多 Sensor Dashboard + 歷史警報統計 + Toast

## 2026-05-03 (Phase 5-8b1) — 整合時域統計設定 + 滑鼠圈選 P-P + Alarm 視覺通知
- **整合「時域統計設定」到「振動量測設定」**：
  - VibrationMeasurementSettingsDialog 新增「時域統計」GroupBox 在最上方
    含 StatisticsHz + StatisticsOverlapPct（沿用既有的 HzItemTpl / PctItemTpl 模板）
  - 振動 mode 下隱藏 ⏱ icon（透過 InverseBoolToVis converter）
  - 水平 / 溫溼度 mode 下 ⏱ icon 仍顯示（原 TimeDomainSettingsDialog 保留）
- **滑鼠圈選計算 P-P**（OxyPlotExt/PpSelectionManipulator.cs）：
  - 觸發：**Shift + 左鍵 拖曳**
  - 行為：拖曳框內以 RectangleAnnotation 顯示半透明橘色框 +
    框內標示每條可見 LineSeries 的 P-P (max - min)
  - 自動清除上一次的 P-P 框（用 Tag="PP" 標記）
  - 切換 mode / sub-mode 時 ResetSweepForCurrentMode 會清除 P-P 框
  - 自定義 IViewCommand `CustomPlotCommands.SelectPp` + 綁 OxyModifierKeys.Shift
  - ⚙ icon tooltip 含快捷鍵說明
- **Alarm 視覺通知**（Services/AlarmToastService.cs）：
  - 黃/紅燈觸發時，主視窗右下角彈出無框 Window（4 秒後自動淡出）
  - 顏色依 level：Red = #E74C3C / Yellow = #F39C12
  - 多個通知垂直堆疊（最新在最下方）
  - 點擊通知立即關閉
  - 純 WPF Window + DoubleAnimation 實作（不需要 NuGet）
- **三語新增 4 keys**：VibSet.StatsSection / VibSet.StatsDesc /
  VibSet.TipWithShortcut / Unit.Sec / Unit.Auto（部分已 5-8b 加過）
- 三語系合計 310 keys 對齊
- **下一刀（Phase 5-8c）**：多 Sensor Dashboard + 歷史警報統計畫面

## 2026-05-03 (Phase 5-8c) — Sensor 自動重連 + 多 Sensor Dashboard + 歷史警報統計
- **Sensor 自動重連**：
  - 新增 ReconnectService（Services/ReconnectService.cs）
    - 監聽 `OnStateChanged`，Disconnected/Faulted 時啟動 retry 協程
    - 每 ReconnectIntervalSec 秒嘗試一次，最多 ReconnectAttempts 次
    - 「user-intended」狀態追蹤：使用者主動斷線 → 不重連
    - StatusChanged event 通知 UI（status bar + toast）
    - ConcurrentDictionary 管理多 sensor，每個獨立 CancellationTokenSource
  - AppSettings 新增 `ReconnectAttempts` (default 5, 0=停用) + `ReconnectIntervalSec` (default 10)
    - SegmentMinutesOptions/RetentionDaysOptions 同模式有 *Options 公開陣列
    - 持久化（settings.json）
  - PreferencesDialog 新增「Sensor 自動重連」GroupBox：
    - 最大重試次數 (0/3/5/10/20/50 次，0 顯示「停用」)
    - 重試間隔 (5/10/15/30/60 秒)
  - MainViewModel 在 Connect/Disconnect 時呼叫 NotifyUserConnect/Disconnect
  - App.OnExit 呼叫 ReconnectService.StopAll()
  - 重連狀態同步到 status bar；成功 / 放棄 也跳 toast（AlarmToastService.ShowSimple 新方法）
- **多 Sensor Dashboard**（Views/SensorDashboardWindow）：
  - 工具列加 ☷ 儀表板 按鈕（非 modal Window，singleton）
  - WrapPanel 卡片顯示所有 SensorTabs：
    - 標題：Sensor name + 連線狀態 chip（綠 / 灰）
    - 三軸：X/Y/Z 各顯示 0-P 大字（顏色依 alarm level）+ RMS 小字
    - 底部：溫溼度 + 三軸角度 + 整體最嚴重 alarm chip
  - ChannelViewModel 加 6 個衍生屬性：
    - AccentBrush / XAlarmBrush / YAlarmBrush / ZAlarmBrush
    - WorstAlarmLevel / WorstAlarmBrush / WorstAlarmText
  - partial method (OnPeakXChanged 等) 觸發衍生屬性更新
  - BoolToConnBrushConverter（綠 / 灰）註冊到 App.xaml
- **歷史警報統計畫面**（Views/AlarmStatsWindow）：
  - 工具列加 📊 警報統計 按鈕
  - 篩選：日期範圍（今日/7天/30天/90天/全部）
  - 主體：
    - 三色 chip（綠解除 / 黃 / 紅 總計）
    - DataGrid：Sensor × 量值 × 黃次 × 紅次（按紅次降冪）
    - OxyPlot 堆疊 BarSeries：每日黃 + 紅 數量
  - 解析 alarm_*.csv：跳過 metadata header，從 "Timestamp," 後開始
  - 簡易 CSV split 支援雙引號 escape
  - 只計「等級轉變」事件（FromLevel != ToLevel）
- **三語新增 33 keys**：Pref.Reconnect.* (4) / Unit.Times,Disabled (2) /
  Toolbar.Dashboard,AlarmStats (2) / Dashboard.* (4) / AlarmStats.* (21)
- 三語系合計 342 keys 對齊
- **Phase 5-8 系列收尾**：核心觀測功能（FFT/Waveform、Dashboard、警報統計、自動重連）全部 ready

## 2026-05-03 (Phase 5-8c) — 自動重連 + 多 Sensor Dashboard + 歷史警報統計
- **自動重連服務**（Services/ReconnectService.cs）：
  - 監聽每個 SensorChannel 的 OnStateChanged 事件
  - User-intended 狀態 → 收到 Disconnected/Faulted 時自動觸發重試協程
  - 預設 5 次，每次 10 秒間隔（可在 Preferences 調整）
  - 0 次 = 完全停用自動重連
  - 每個 Sensor 獨立的 CancellationTokenSource，user 手動斷線即取消
  - 重試成功 / 失敗放棄都透過 StatusChanged event 通知 UI
- **AppSettings 加 ReconnectAttempts (default 5) + ReconnectIntervalSec (default 10)**
  - 持久化到 settings.json
  - Options：[0/3/5/10/20/50] 次 + [5/10/15/30/60] 秒
- **MainViewModel 整合**：
  - 每個 SensorTab 創建後 → ReconnectService.Register(tab)
  - 同步連線成功 → NotifyUserConnect(idx)
  - 同步斷線 → NotifyUserDisconnect(idx)
  - 訂閱 StatusChanged → 寫到 status bar + 成功/失敗時 ShowSimple toast
  - App.OnExit → ReconnectService.StopAll()
- **PreferencesDialog 加自動重連 GroupBox**（次數 + 間隔，IntLabelItem 包裝顯示單位）
- **AlarmToastService 加 ShowSimple(message, isError)** 支援非警報訊息
- **多 Sensor Dashboard**（Views/SensorDashboardWindow.xaml）：
  - WrapPanel 卡片佈局，每張卡 340x300
  - 顯示：Sensor 名 + 連線狀態 chip / X/Y/Z 三軸 0-P 大字 + 各自 Alarm 顏色 + RMS / 溫溼度 / 三軸角度 / 整體 Worst alarm chip
  - 直接 binding ObservableCollection<SensorTabViewModel>，數值即時跟著主視窗更新
  - 同一 dashboard window 重複開只 Activate（避免多份）
- **ChannelViewModel 加 Dashboard 衍生屬性**：
  - AccentBrush / XAlarmBrush / YAlarmBrush / ZAlarmBrush
  - WorstAlarmLevel / WorstAlarmBrush / WorstAlarmText
  - Peak/RMS/Angle 變動時 partial method 觸發 OnPropertyChanged 連動
- **歷史警報統計**（Views/AlarmStatsWindow.xaml）：
  - 工具列「📊 警報統計」按鈕
  - 時間範圍篩選（今日 / 近 7 天 / 近 30 天 / 全部）
  - 三個總計卡片（綠 / 黃 / 紅）
  - 每日警報數量堆疊柱狀圖（Yellow + Red Stacked BarSeries）
  - DataGrid：Sensor × 量值組合的 Yellow / Red 計數
  - CSV reader 自動跳過 Phase 5-7a 加的 metadata header 區
- **MainWindow 工具列加 ☷ Dashboard / 📊 警報統計 兩個按鈕**
- **BoolToConnBrush converter** 支援連線狀態 chip 顏色
- **三語新增 21 keys**（Pref.Reconnect.* / Dashboard.* / AlarmStats.* / Unit.Times / Unit.Disabled / Toolbar.Dashboard / Toolbar.AlarmStats）
- 三語系合計 342 keys 對齊

## 2026-05-03 (Phase 5-8c2) — 重連 Bug 修復 + Toast 過濾 + 波形 Y 軸 + 軟體狀況監控
- **🐛 Bug 修復：拔 USB 後無法自動重連**：
  - 根因：`UsbCdcTransport.OnPortDataReceived` catch 內只 ChangeState(Faulted)
    但 `_port` SerialPort handle 沒釋放 → 下次 ConnectAsync 對同一 PortName Open() 失敗
    Windows 不允許同 COM port 同時兩個 handle
  - 解法：catch 內呼叫 Cleanup()；ConnectAsync 開頭也保險呼叫一次 Cleanup()
- **波形 Y 軸獨立可調**（5-8c2）：
  - 新增 `WaveformYMaxG` 屬性（預設 2G，從原本沿用 VibYMaxG 改為獨立）
  - 振動量測設定加「Y 軸範圍」ComboBox（Auto / 0.5 / 1 / 2 / 5 / 16 G）
  - 持久化到 settings.json
- **Toast 視覺通知過濾**：
  - 新增 `AlarmToastEnabled`（主開關，預設 On）
  - 新增 `AlarmToastOnYellow`（預設 false）
  - 新增 `AlarmToastOnRed`（預設 true）
  - `AlarmToastService.Show` 內依設定 filter
  - PreferencesDialog 加「警報視覺通知」勾選區（含黃/紅獨立選項）
- **斷線重連 UI 增強**：
  - 新增 `AlarmToastService.ShowReconnect(sensorName, message, status)`
    - status: "reconnecting" / "success" / "giveup"
    - 同 sensor 重複呼叫會更新既有 popup 內容（不會堆疊多個）
    - "reconnecting" 不自動消失（持續顯示重試進度）
    - "success" / "giveup" 30 秒後自動淡出
    - 顏色：橘色重試中 / 綠色成功 / 紅色放棄
  - `ReconnectService.NotifyStatus` 同步呼叫 ShowReconnect
- **軟體狀況監控（Event Log）**：
  - 新增 `ErrorLogService`：Info / Warn / Error 三級
  - 寫到 `%LocalAppData%\Tranzx.iVS4\Logs\app_yyyyMMdd.csv`
  - 記憶體保留最近 500 筆給 UI 顯示
  - `EventLogWindow`：DataGrid 顯示時間/等級/來源/訊息（等級欄位有色 chip）
  - 工具列加「⚠ 狀況監控」按鈕
  - 啟動 / 結束 / 重連嘗試 / 重連成功 / 重連放棄都會記錄
- **三語新增 12 keys**（Pref.Toast.* / VibSet.WaveformY / EventLog.* / Toolbar.EventLog）
- 三語系合計 354 keys 對齊
