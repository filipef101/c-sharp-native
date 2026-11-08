# KVM and the API 34 emulator

Checked before any `cnative` or `dotnet build -t:Run`. Kernel `6.12.113+`, `/proc/sys/kernel/tainted` was `0` before the probe and still `0` after the guest reached `sys.boot_completed=1`. `dmesg` had no `kernel BUG` line during this boot.

## Device node

`/dev/kvm` was `crw-rw---- root 103`. The user was not in group 103. Opening it returned `Permission denied`.

```
open_fail [Errno 13] Permission denied: '/dev/kvm'
```

`sudo chmod 666 /dev/kvm` made the node `crw-rw-rw-`. The same open then returned a file descriptor. This matches the earlier note in `docs/devx/comparison.md` for this kernel. It is a host permission, and KVM itself answered after the mode change.

## API, VM, vCPU

A probe compiled against `/usr/include/linux/kvm.h`:

```
api=12 expected=12
vm=4
vcpu=5
kvm_ok
```

Exit 0. Tainted stayed `0`.

## `emulator -accel-check`

`2026-11-08T19:47:14Z`, elapsed about 0.06 s, exit 0:

```
accel:
0
KVM (version 12) is installed and usable.
accel
```

## Boot

AVD `poc34` created with `avdmanager` for `system-images;android-34;google_apis;x86_64`, device `pixel_6`. `config.ini` had `hw.cpu.arch=x86_64`, `hw.lcd.width=1080`, `hw.lcd.height=2400`.

The repo docs do not say how to create or start an AVD. The command below is the one recorded in `docs/devx/comparison.md` for an earlier boot of this image. A newcomer is not told to run it. See finding F8.

```
BOOT_START 2026-11-08T19:47:27Z
emulator -avd poc34 -accel on -gpu swiftshader_indirect -memory 2048 -cores 2 -no-snapshot -no-window -no-audio -no-boot-anim -port 5554
```

Emulator log:

```
INFO         | Android emulator version 37.2.13.0 (build_id 16740729) (CL:N/A)
INFO         | Increasing RAM size to 2560MB
INFO         | Boot completed in 40789 ms
```

The qemu command line contained `-accel on`. After boot:

| Check | Value |
| --- | --- |
| `adb devices` | `emulator-5554 device` |
| `sys.boot_completed` | `1` |
| `ro.build.version.sdk` | `34` |
| `ro.product.cpu.abi` | `x86_64` |
| tainted | `0` |

`dmesg` during boot included the same two lines seen on the earlier successful boot in `docs/devx/comparison.md`, and no BUG:

```
kvm: SMP vm created on host with unstable TSC; guest TSC will not be reliable
kvm: kvm [6343]: vcpu0, guest rIP: 0xffffffffb4a03a51 Unhandled WRMSR(0xc2) = 0xffff
```

KVM was usable. The evaluation continued.
