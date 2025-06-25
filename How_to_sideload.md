# Meta Quest: Developer Setup and APK Sideloading Guide

This guide explains how to:

* Create a Meta developer organization
* Enable Developer Mode on your Quest 3
* Install and use ADB to sideload APKs

---

## 1. Create a Developer Organization

You must register as a developer with Meta before enabling Developer Mode.

Steps:

1. Visit: [https://developers.meta.com/horizon/manage/organizations/create/](https://developers.meta.com/horizon/manage/organizations/create/)
2. Log in using your Meta account
3. Click “Create New Organization”
4. Accept the developer agreement
5. Enter an organization name
6. Submit the form to complete registration

---

## 2. Enable Developer Mode on Quest

You need the Meta Quest mobile app and your headset must be paired.

Steps:

1. Open the Meta Horizon mobile app.
2. Navigate to Menu > Devices and then select your device.
3. Go to Headset Settings > Developer Mode and then enable Developer Mode.

If Developer Mode does not appear, ensure your account is registered as a developer (step 1).

---

## 3. Install ADB (Android Debug Bridge)

### Option 1: Download Android Platform Tools

1. Go to: [https://developer.android.com/tools/releases/platform-tools](https://developer.android.com/tools/releases/platform-tools)
2. Download and extract the ZIP
3. Note the path to the extracted folder

For Ubuntu/Debian (I did not test this):

```
sudo apt update
sudo apt install android-tools-adb
```

---

## 4. Connect Your Quest to the Computer

1. Connect the Quest 3 to your PC using a USB-C cable
2. Put on the headset
3. When prompted, allow USB debugging
4. (Optional) Check "Always allow from this computer"

---

## 5. Sideload the APK

1. Open a terminal or command prompt at the ADB location
2. Verify connection:

```
adb devices
```

The output should list your Quest device with the word "device" next to it. If it says "unauthorized," check your headset for a prompt.

3. Install the APK:

```
adb install [path_to_VR.apk]
```

To reinstall or update:

```
adb install -r [path_to_VR.apk]
```

---

## 6. Launch the Sideloaded App

1. Put on the headset
2. Go to "Apps"
3. Select "Unknown Sources"
4. Launch your app from the list
