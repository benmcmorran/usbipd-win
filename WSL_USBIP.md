<!--
SPDX-FileCopyrightText: Microsoft Corporation

SPDX-License-Identifier: GPL-2.0-only
-->

# Setting up USBIP on WSL 2

Update WSL:

```pwsh
wsl --update
```

List your distributions.

```pwsh
wsl list
```

Export current distribution to be able to fall back if something goes wrong.

```pwsh
wsl --export Ubuntu-20.04 <temporary-distro-path>\Ubuntu-usbip.tar
```

Import new distribution with current distribution as base.

```pwsh
wsl --import Ubuntu-usbip <temporary-distro-path>\Ubuntu-usbip.tar <temporary-distro-path>\Ubuntu.tar
```

Run new distribution.

```pwsh
wsl --distribution Ubuntu-usbip --user <user>
```

Update resources.

```bash
sudo apt update
```

Install prerequisites.

```bash
sudo apt install build-essential flex bison libssl-dev libelf-dev libncurses-dev autoconf libudev-dev libtool
```

Clone kernel that matches wsl version. To find the version you can run.

```bash
uname -r
```

The kernel can be found at: <https://github.com/microsoft/WSL2-Linux-Kernel>

After finding the branch/tag that matches the version, clone that branch/tag.

```bash
sudo git clone https://github.com/microsoft/WSL2-Linux-Kernel.git /usr/src/5.4.72-microsoft-standard-WSL2 
cd /usr/src/5.4.72-microsoft-standard-WSL2  
git checkout linux-msft-5.4.72
```

Copy current configuration file.

```bash
/usr/src/4.19.43-microsoft-standard$ sudo cp /proc/config.gz config.gz
/usr/src/4.19.43-microsoft-standard$ sudo gunzip config.gz
/usr/src/4.19.43-microsoft-standard$ sudo mv config .config
```

Run menuconfig to select kernel features to add. The number 4 is the number of cores I have.

```bash
sudo make menuconfig
```

Select desired features. In my case I selected USB/IP, VHCI HCD, Debug messages for USB/IP, USB Serial Converter Support.
In the following command the number '8' is the number of cores I will be using.

```bash
sudo make -j 8 && sudo make modules_install -j 8 && sudo make install -j 8
cp arch/x86/boot/bzImage /mnt/c/Users/<user>/usbip-bzImage
```

Build USBIP tools.

```bash
/usr/src/5.4.72-microsoft-standard$ cd tools/usb/usbip
/usr/src/5.4.72-microsoft-standard/tools/usb/usbip$ sudo ./autogen.sh
/usr/src/5.4.72-microsoft-standard/tools/usb/usbip$ sudo ./configure
/usr/src/5.4.72-microsoft-standard/tools/usb/usbip$ sudo make install -j 12
```

Copy tools libraries location so usbip tools can get them.

```bash
sudo cp libsrc/.libs/libusbip.so.0 /lib/libusbip.so.0
```

Install usb.ids so you have names displayed for usb devices.

```bash
sudo apt-get instal hwdata
```

Copy image.

```bash
cp arch/x86/boot/bzImage /mnt/c/Users/<user>/usbip-bzImage
```

Create a `.wslconfig` file on `/mnt/c/Users/<user>/` and add a reference to the created image with the following.

```ini
[wsl2]
kernel=c:\\users\\t-nelsont\\configurations\\wsl-new
```

Your WSL distro is now ready to use!
