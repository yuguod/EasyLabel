示例数据说明
============

sample-data.xlsx / sample.csv
  - 三列：文本、条码、二维码
  - 导入时选择“首行为标题行”，映射三列即可
  - 条码列建议在 Excel 中设置为“文本”，避免前导零丢失

demo-40x15.zpl / demo-30x25.zpl / demo-30x20.zpl
  - 由本程序生成的示例 ZPL（40×15、30×25 为 203 dpi；30×20 为 600 dpi）
  - 可用于 Labelary 预览或直接发送到支持 ZPL 的打印机

demo-40x15-preview.png / demo-30x25-preview.png / demo-30x20-preview.png
  - 对应 ZPL 的 Labelary 渲染预览（尺寸以打印机实际输出为准）

快速验证
--------
1. 打开程序 → 批量打印 → 导入 sample-data.xlsx
2. 勾选三行 → 打印（需先在“打印机设置”里选择打印队列或填写网络 IP）
3. 首次使用先打印“打印机设置 → 测试页”，用尺子核对尺寸
