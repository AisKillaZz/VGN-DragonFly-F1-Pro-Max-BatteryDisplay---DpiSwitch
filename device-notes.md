# 当前电脑检测到的设备

使用 Windows `pnputil /enum-devices /connected /ids` 只读查询到：

- USB VID/PID：`3554:F503`
- USB revision：`0109`
- 复合设备接口：`MI_00` 键盘、`MI_01` 厂商自定义 HID、`MI_02` 鼠标
- `MI_01` 下出现了厂商 usage page `FF05`、`FF04/usage 0002`、`FF03`、`FF02/usage 0002`

这比只按“标准鼠标接口”查找更重要：电量和 DPI 通常会放在 `MI_01` 的 vendor-defined collection 里。后续协议抓包时应优先选择 `VID_3554:PID_F503` 且 `UsagePage >= 0xFF00` 的 HID collection。

注意：这些是设备枚举信息，不是电量/DPI 报文格式；在没有确认 report ID 和校验方式前不要向设备写入 feature/output report。
