# 圆润软件 Logo

`rounded-logos-v1.png` 是 v0.8.0 使用的透明图集，随构建嵌入 EXE，运行时不联网。`rounded-logos-reference.png` 是保留的设计参考，不嵌入程序。

图集为 4 列 × 2 行，按行排列：Discord、Steam、WeChat、Edge、NVIDIA、Firefox、OneDrive、Spotify。按实际图片宽高整数分区，不能假定各格恰好都是同一整数尺寸。每格保留透明背景和完整标志，绘制时等比缩放。图片原始文件不在运行时覆写。

这是 CCD Scheduler 的自定义显示素材，保留各软件的辨识特征；不修改第三方 EXE。`RoundedLogoPack` 只依据明确的 EXE 名称及公司、产品字段选择显示素材。无法匹配或缺少原图时继续使用原有显示；WebView2 不作为 Edge。显示资源不参与应用身份、原图相似度分组、游戏分类、选择快照或 CPU 规则。

生成方式：内置 **imagegen**，对设计参考做图像编辑，`transparent_background=true`。没有使用 CLI 或用户 API Key。生成图像存在随机性；正常构建直接使用已保存 PNG，无需重新生成。

最终图集编辑提示词如下（参考文件为本目录的 `rounded-logos-reference.png`）：

> Edit target: the supplied single logo design board. Turn this EXISTING artwork into ONE production-ready transparent sprite atlas for a desktop app; keep all eight logo designs and their recognizable features. Remove ALL headings and ALL text labels. Remove the white canvas background completely, with real alpha transparency, while preserving intentionally white details inside the Steam, WeChat, Discord, NVIDIA etc. marks. Rearrange the EXISTING eight marks into an exact 4-column x 2-row regular grid covering the whole canvas, ideally 2048x1024. Each grid cell is an equal square; center its logo optically in the exact center of that square with an empty transparent margin of at least 15 percent on every side. Keep the full artwork inside its own cell. Row one in order: Discord, Steam, WeChat, Edge. Row two in order: NVIDIA, Firefox, OneDrive, Spotify. No additional objects. Retain the reference's rounded simplified glyph shapes, familiar brand colors and similar optical weight. No tile or card backgrounds, no shadows, no labels, no heading, no text anywhere. Only the eight freestanding marks with transparent gutters between cells and transparent canvas edges. This atlas will be embedded offline in the software; it must not be a screenshot or design presentation.

实际输出为 1774 × 887；分区和透明度以素材验证结果为准。
