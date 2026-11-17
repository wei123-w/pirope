# Third-party notices

SnipLite's original source code is licensed under the MIT License. Dependencies retain their own licenses.

## SimdPaddleOCR and embedded PP-OCRv6 Small models

| Package | Version | License |
| --- | --- | --- |
| Sdcb.SimdPaddleOCR | 2.0.0 | Apache-2.0 |
| Sdcb.SimdPaddleOCR.ModelProvider | 1.0.0 | Apache-2.0 |
| Sdcb.SimdPaddleOCR.Models.ChineseV6Small | 1.0.0 | Apache-2.0 |
| Sdcb.SimdPaddleOCR.Models.TextLineOrientation | 1.0.0 | Apache-2.0 |

Copyright (c) 2026 Sdcb.PaddleOCR contributors. The bundled PP-OCRv6 DET/REC, text-line orientation CLS and dictionary originate from the PaddleOCR ecosystem: Copyright (c) 2016 PaddlePaddle Authors. All Rights Reserved.

- Upstream: https://github.com/sdcb/SimdPaddleOCR and https://github.com/PaddlePaddle/PaddleOCR
- License: [Apache License 2.0](licenses/SimdPaddleOCR-Apache-2.0.txt)
- Original model attribution and sources: [upstream third-party notices](licenses/SimdPaddleOCR-THIRD-PARTY-NOTICES.txt)

SnipLite uses the CPU inference path. The models are embedded in their package assemblies; no external model service or API key is required. PaddleOCR and PP-OCR names belong to their respective owners; SnipLite is not endorsed by them.

## ZXing.Net and Windows Compatibility bindings

| Package | Version | License |
| --- | --- | --- |
| ZXing.Net | 0.16.11 | Apache-2.0 |
| ZXing.Net.Bindings.Windows.Compatibility | 0.16.17 | Apache-2.0 |

Maintained by Michael Jahn and contributors. ZXing.Net is a port of the ZXing barcode library.

- Upstream: https://github.com/micjahn/ZXing.Net
- License text: [Apache License 2.0](licenses/ZXing-Apache-2.0.txt)

## .NET and Windows components

SnipLite uses the .NET runtime, Windows desktop framework, and Windows accessibility APIs. Windows system components are provided by the operating system and are not included in this source repository.

.NET license and third-party notices are included under `licenses/dotnet/`. A self-contained publish also includes .NET runtime binaries; their original licensing terms continue to apply.

- .NET: https://github.com/dotnet/runtime
- Windows Forms: https://github.com/dotnet/winforms

## UI Automation COM declarations

`Interop.UIAutomationClient` 10.19041.0 provides the COM declarations used to query the operating system's UI Automation service for automatic capture targets. Copyright (c) 2019 Roman. Licensed under MIT. The UI Automation service itself is supplied by Windows.

- Upstream: https://github.com/FlaUI/UIAutomation-Interop
- License: [MIT License](licenses/UIAutomation-Interop-MIT.txt)

The application no longer uses Windows OCR, Windows SDK .NET projections, or the WPF automation client. The self-contained distribution retains the .NET and Windows Forms components required by its SDK dependency graph.

## Windows installer

The setup executable is built with Inno Setup 6.7.4, copyright Jordan Russell and Martijn Laan. The compiler is a build tool and is not included in this source repository. Inno Setup's original installer notices are preserved.

- Upstream: https://jrsoftware.org/
- License: [Inno Setup License](licenses/Inno-Setup-LICENSE.txt)
