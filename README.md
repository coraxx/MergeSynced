# README #

## Overview ##
<!-- TOC -->

- [README](#readme)
    - [Overview](#overview)
    - [About](#about)
    - [Requirements](#requirements)
    - [License](#license)

<!-- /TOC -->

* * *
## About ##

Initially build for synchronizing a multi camera and microphone setup, this tool was extended to merge streams with the determined offset.

The offset between two audio streams is calculated by cross correlating a part of the selected audio streams. The result can be used wherever needed, or a new file can be generated with the selected audio/video streams.

<img src="https://semper.space/MergeSynced/Screenshot_2_1_0-light.png"  width="600">

<img src="https://semper.space/MergeSynced/Screenshot_2_1_0-dark.png"  width="600">

* * *
## Requirements ##
At least ffmpeg needs to be installed. Depending on the container format used, mkvtoolnix needs to be installed as well.
The executables must be made available through the PATH environment variable, otherwise this application will not be able to find them.

Depending on the used platform there are multiple ways to install ffmpeg and mkvtoolnix.
It could look something like:
```
Linux # sudo apt-get install mkvtoolnix
Mac   # brew install ffmpeg mkvtoolnix
Win   # choco install ffmpeg mkvtoolnix
```
<br />
<br />

* * *
## License ##

The MIT License (MIT)
Copyright (c) 2023 Jan Arnold

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
