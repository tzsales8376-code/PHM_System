# Tranzx iVS 4.0 — Lessons Learned

## 從 TZ_ACC_Tester v1.5 繼承的教訓（避免重蹈覆轍）

### L01 — USB CDC Stop/Start 流程必須等待 ACK
- **症狀**：設定 ODR/FS 後，數據流停止無法恢復
- **根因**：USB CDC 模式下，發送 `stop\r\n` 後若立刻發 `CMD A4` 會造成設備 buffer 衝突
- **解法**：`stop\r\n` → 等 50ms → 發送設定指令 → 等 50ms → `start\r\n`
- **相關檔案**：`UsbCdcTransport.SendConfigCommand()`

### L02 — 預設量程必須從設備讀取，不能假設
- **症狀**：靜置 Z 軸顯示 2.05G（應為 1.0G）
- **根因**：軟體預設 ±16G (0.488 mG/LSB)，設備實際是 ±8G (0.244 mG/LSB)
- **解法**：連線後立即發 `CMD A7 (Read FS)`，根據回應動態設定 ScaleFactor
- **相關檔案**：`PacketParser.ScaleFactor`、`SensorChannel.OnConnected()`

### L03 — ODR 設定回應與實測 SPS 可能不同步
- **症狀**：設定 ODR=833Hz，metadata 寫 833，但實測 SPS=3632
- **根因**：韌體可能拒絕 ODR 變更或回應 OK 但未實際套用
- **解法**：FFT 頻率軸統一使用實測 SPS（`SpsSmoother`），UI 同時顯示設定值與實測值
- **相關檔案**：`FftAnalyzer`、`SensorChannel.SpsSmoothed`

### L04 — 序號跳號是丟包的最可靠指標
- **症狀**：Overflow 旗標可能是上一包殘留，不可靠
- **根因**：Overflow 是設備端 buffer 狀態，不代表 PC 收到完整資料
- **解法**：用 `SeqNo` 跳號計算丟包數，比 Overflow 更準確
- **相關檔案**：`PacketParser.LastSeqNo`、`SensorChannel.LostPackets`

### L05 — Smart USB Hub Interlock 模式會限制同時使用通道數
- **症狀**：插第 3 個 Sensor 時前面的會被關閉
- **根因**：Hub 韌體預設可能在 Interlock 模式
- **解法**：應用啟動時自動寫入 Normal 模式 + 全 CH 開啟
- **相關檔案**：`SmartHubService.EnsureNormalMode()`

## iVS 4.0 新增風險點（待驗證）

### L06 — 4 通道全速 USB 頻寬警告
- **預期問題**：4 通道 × 3332 Hz × 38 samples × 6 bytes = ~3 KB/s/ch ≈ 12 KB/s 總量
- 雖在 USB Full Speed (12 Mbps) 範圍內，但 Smart Hub 共用 USB 控制器可能成瓶頸
- **驗證方法**：Phase 1 完成後用 4 個 Sensor 同時跑 ODR=3332 連續 1 小時，看丟包率

### L07 — 嚴格時鐘同步的累積漂移
- **預期問題**：4 個 Sensor 各自時鐘晶振有 ±20 ppm 偏差
- 1 小時下漂移可達 ±72 ms，跨通道分析會出問題
- **緩解**：每 60s 重新發 `CMD A2` 同步，並在 metadata 紀錄同步事件

## Phase 2 學到的事

### L08 — CommunityToolkit.Mvvm `[RelayCommand]` 自動去除 Async 後綴
- **症狀**：XAML `Command="{Binding ConnectAllAsyncCommand}"` 找不到屬性
- **根因**：`[RelayCommand] async Task ConnectAllAsync()` 自動產生 `ConnectAllCommand`（去掉 Async）
- **解法**：XAML 一律使用去掉 Async 後綴的命名

### L09 — Class Library 不能設為啟動專案
- **症狀**：F5 出現「無法直接啟動輸出類型為類別庫的專案」
- **根因**：Visual Studio 預設選擇 .sln 中第一個 Project 為啟動專案，原始 .sln 把 Tranzx.iVS4.Core (類別庫) 排在第一位
- **臨時解法**：右鍵 `Tranzx.iVS4.App` → 設為啟動專案（VS 會記在 `.vs/<sln>/v17/.suo` 中）
- **永久解法**：將 `Tranzx.iVS4.App` 移到 .sln 的第一個 Project 位置（已於 Phase 2 修正）
- **再次發生原因**：解壓新 zip 時若覆蓋掉 `.vs/` 資料夾，啟動專案設定會遺失

### L10 — BLE Stub 事件抑制 CS0067 警告
- **症狀**：`BleTransport.OnDataReceived` 從未使用警告
- **根因**：Phase 4 stub 期間事件不會被觸發但要保留介面契約
- **解法**：`#pragma warning disable CS0067` 包圍事件宣告，Phase 4 實作時移除

## Phase 3 學到的事

### L11 — 命令回應與資料封包共用同一條流（USB CDC）
- **症狀**：在 streaming 中送 ReadFs/ReadOdr，回應被 PacketParser 當垃圾位元組丟掉
- **根因**：PacketParser 對非 0x45 開頭的位元組 `RemoveAt(0)` 直接丟棄
- **解法**：在 `SensorChannel` 加 `_rawMode` flag，讀取命令回應期間暫停 `Parser.Feed`，由 raw sink 收集
- **延伸**：因為要切換 mode，所以指令必須在 stream stop 時才能可靠讀回；`ApplyConfigAsync` 已整合 stop → set → verify → start

### L12 — 韌體回應格式可能尚未統一
- **症狀**：ReadFs/ReadOdr 回應到底是 binary 還是 ASCII？v1.4 規格未明確
- **解法**：`ResponseParser` 雙格式並列嘗試，皆解析不到時回傳 null
- **使用面**：UI 顯示「韌體未回應 ReadFs/ReadOdr」並非錯誤，而是韌體不支援回讀指令的友善訊息

### L13 — Recipe 跨機器移轉時 PortName 失效
- **症狀**：在 A 機器存的 Recipe (COM4)，到 B 機器可能 COM6
- **解法**：載入時不立即連線，而是 Attach 配置 → 使用者手動驗證 / 修正 → 按連線
- **未來**：Phase 4 可加入「Recipe 載入後自動嘗試以 SensorID 配對 USB 裝置」

## Phase 4 學到的事

### L14 — DynamicResource 自動更新但 Converter 不會
- **症狀**：切換語系後，工具列等 `{DynamicResource Toolbar.AddChannel}` 文字
  立刻更新，但通道卡片上的「已連線」「未連線」等透過 `TransportStateToTextConverter`
  產生的文字仍是舊語系
- **根因**：`DynamicResource` 是 WPF 內建的 binding 機制，會主動監聽 Resource 變更；
  但 `IValueConverter` 的 binding 來源是 `TransportState` 列舉值，未變更就不會重新呼叫 Convert
- **解法**：在 `ChannelViewModel` 訂閱 `LocalizationService.LanguageChanged`，
  收到事件後 `OnPropertyChanged(nameof(State))` 強制 binding 重新計算
- **延伸**：所有用 Converter 把 enum / 數值轉成字串的 binding 都需要這個處理；
  動態字串（status 訊息、verification label）則改由 ViewModel 直接 `Loc.Format()`
  並訂閱 LanguageChanged 重算

### L15 — i18n 動態字串使用 placeholder 而非字串拼接
- **症狀**：用「已連線 」+ count + " 通道" 的方式拼接，到日文會變成「已連線 4 通道」
  順序不通，因為日文是「4 チャンネル接続済み」（數字在前）
- **根因**：不同語言的語序差異
- **解法**：所有動態字串使用 `{0}` `{1}` placeholder + `string.Format()`：
  - zh-TW: `已連線 {0}/{1} 個通道`
  - en:    `Connected {0}/{1} channels`
  - ja:    `{0}/{1} チャンネル接続済み`
- **約定**：含 placeholder 的 resource key 後綴 `Fmt` 方便辨識，例如
  `Status.ConnectingFmt`、`Verification.SuccessFmt`、`Env.SeriesTempFmt`

### L16 — OxyPlot 不支援 DynamicResource
- **症狀**：`PlotModel.Axes[i].Title = "頻率 (Hz)"` 是普通屬性，無法用
  `{DynamicResource Vibration.AxisFreq}` 綁定
- **解法**：在 View code-behind 訂閱 `LanguageChanged`，收到事件後
  讀取目前 Loc 字串再呼叫 `model.InvalidatePlot(false)`
- **位置**：`VibrationTabView.UpdateAxes()`、`EnvTabView.ApplyLocalization()`

### L17 — 不要假設韌體需要 wake-up 命令；尊重設備預設行為
- **症狀**：USB 連線成功，113K bytes 持續流入，但 PacketParser 找不到 `0x45` header，
  ValidPackets=0，圖表上只有殘影
- **根因**：我在 `ApplyConfigAsync` 一連串 `stop\r\n` → SetFs (binary 0xCC...) →
  SetOdr → ReadFs → ReadOdr → `start\r\n` 這些命令把韌體**從預設的「241B/0x45 串流模式」
  推進到一個未知子模式**，韌體開始輸出 raw 6-byte BE (Z, X, Y) int16 樣本（沒有包頭）
- **驗證**：v1.5 校正工具能連同款韌體；hex preview 顯示 `07 DE 00 ?? 00 ??` 重複，
  `07 DE` 在 BE 解讀為 0x07DE = 2014 LSB，對 ±16G 量程正好是 ~1G（重力，設備平放）
- **解法**：`ApplyConfigAsync` 改為 **No-op**（只設定 `ScaleFactor` 不送任何指令），
  韌體預設就在串流 241B 封包，PacketParser 會自動對齊 `0x45`
- **教訓**：在不確定韌體支援哪些指令時，**寧可什麼都不送**，比把它推進錯誤狀態好。
  動態變更 FS/ODR 是 Phase 5+ 的「進階」功能，需要先有韌體規格手冊或可重複的指令格式驗證

### L18 — XAML 前向 ElementName binding 可能讓 x:Name 欄位變 null
- **症狀**：`Border` 的 Visibility 透過 `{Binding ElementName=tbCursorInfo, Path=Text, ...}`
  綁到後面才宣告的 `<TextBlock x:Name="tbCursorInfo" ...>`，runtime 點圖表觸發
  `ClearCursor()` 時噴 `NullReferenceException`：tbCursorInfo 為 null
- **可能根因**：XAML 編譯器解析到 Border Visibility 綁定時，後面的 TextBlock 還沒處理，
  自動產生的 .g.cs 中欄位賦值順序異常導致 tbCursorInfo 欄位未被指派
- **解法**：
  1. 不要用前向 `ElementName` binding（特別是在 Visibility / IsEnabled 等屬性上）
  2. 改為 code-behind 直接控制：把 Border 也加 `x:Name="brdCursorInfo"`，
     寫一個 `SetCursorInfo(text)` helper 同時處理 Text 與 Visibility
  3. 加 null guard：`if (tbCursorInfo is not null) tbCursorInfo.Text = ...`
- **慣例**：XAML element 互相引用優先用「向後引用」（被引用者在前），
  或者乾脆用 code-behind 控制，避免 ElementName 隱含的解析時序假設

### L19 — 動態 FontSize 用 ResourceDictionary 覆寫，配合 DynamicResource
- **症狀**：希望使用者能即時切換字型大小，但 XAML 的 FontSize 是寫死的數值
- **解法**：
  1. 在 `App.xaml` 預先註冊 `<sys:Double x:Key="RootFontSize">13</sys:Double>` 等
     一組命名 FontSize 資源（搭配 `xmlns:sys="clr-namespace:System;assembly=System.Runtime"`）
  2. XAML 全用 `FontSize="{DynamicResource RootFontSize}"`（**必須是 DynamicResource，不能是 StaticResource**）
  3. `AppSettingsService.ApplyFontScale()` 透過 `Application.Current.Resources["RootFontSize"] = newValue;`
     直接覆寫，所有 DynamicResource 自動更新
- **注意**：StaticResource 是編譯期解析，覆寫 Application.Resources 不會生效；DynamicResource 才會
- **副作用**：改變 FontSize 會觸發 layout pass，建議搭配 ComboBox 讓使用者明確選擇
  (Small=11/Normal=13/Large=15/X-Large=17)，不要根據視窗大小自動變動

### L20 — ResourceDictionary 重複 x:Key 在 runtime 才會炸
- **症狀**：App 啟動立刻 `XamlParseException: 設定屬性 'System.Windows.ResourceDictionary.DeferrableContent' 時擲回例外狀況`，
  停在 App.xaml 行 3 位置 21（xmlns:x 那行 — **誤導**，實際錯在 MergedDictionaries 的子檔）
- **根因**：Strings.zh-TW.xaml（含 en / ja 同步）有兩處 `x:Key="Toolbar.StopRecord"`，
  分別來自 Phase 4 Log Settings 與 Phase 5-1 工具列重排兩次新增。
  XAML 編譯期不檢查重複 key（兩個 XAML build 各自合法），但 runtime
  ResourceDictionary 載入時會丟例外
- **解法**：每次新增 resource keys 後跑：
  ```
  grep -oE 'x:Key="[^"]+"' Resources/Strings.*.xaml | sort | uniq -c | awk '$1 > 1'
  ```
- **慣例**：把這個 dup-check 加進 changelog 前的 validation 步驟，跟「missing keys」一起做

### L21 — CommunityToolkit.Mvvm partial method 參數名必須是 `value`
- **症狀**：CS8826 警告 8 個（每個 [ObservableProperty] 對應的 OnXxxChanged 都警告），
  訊息「部分方法宣告 'OnShowXChanged(bool value)' 和 'OnShowXChanged(bool v)' 有簽章差異」
- **根因**：CommunityToolkit.Mvvm 的 source generator 產生的 partial method 簽章固定用
  `bool value`（generator 慣例，與屬性 setter 的 `value` 一致）。我手寫的
  hook 用 `bool v` 雖然功能上一樣，但編譯器視為簽章不一致 → CS8826 警告
- **解法**：永遠用 `value` 作為參數名：
  ```csharp
  [ObservableProperty] private bool showX;
  partial void OnShowXChanged(bool value) => DoSomething();   // ← 必須是 value
  ```
- **快速檢查**：
  ```
  grep -rn 'partial void On.*Changed(bool [^v])' src/Tranzx.iVS4.App/
  ```

## L25 (2026-05-03 Phase 5-7c)
- **症狀**：點「⚙ 錄製設定」按鈕，App 整個閃退，跳到 VS 在
  `var dlg = new Views.RecordingSettingsDialog` 那行
- **根因**：Window.Resources 內定義的 DataTemplate 含 `<Run Text="{DynamicResource ...}"/>`
  - Run 是 Inline（FrameworkContentElement），不是 FrameworkElement
  - 被 ComboBox.ItemTemplate 動態實例化時，Run 的 inheritance context
    跟 LogicalTree 連結不可靠
  - DynamicResource 找不到 resource，構造 dialog 階段就 throw
  - 對比：直接寫在 TextBlock 內的 `<Run Text="{DynamicResource X}"/>` 沒事
    （在 LogicalTree 上有可達路徑）
- **解法**：拔掉 DataTemplate，改用 code-behind 包裝物件
  ```csharp
  private sealed record DurationItem(int Value, string Suffix)
  {
      public override string ToString() => $"{Value} {Suffix}";
  }
  cmb.ItemsSource = options.Select(m => new DurationItem(m, loc["Recording.Min"]));
  ```
  本地化字串只在 dialog 構造時 evaluate 一次，沒有 DynamicResource 解析時機問題
- **教訓**：**`<Run Text="{DynamicResource ...}"/>` 千萬別寫在 DataTemplate 內**。
  ItemTemplate 想顯示帶單位的字串：用 `Binding StringFormat={}{0} unit`，
  或 code-behind 包裝 record + override ToString()
