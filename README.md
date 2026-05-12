# Big Ambitions Modding SDK

Create, build, install, and upload mods for **Big Ambitions** using Unity.

---

# Requirements

- Unity Hub
- Unity **2022.3.62f2**
- macOS Build Support module installed through Unity Hub
- A C# IDE:
  - Visual Studio
  - Rider
  - VS Code
- Big Ambitions installed through Steam
- Git client

---

# Getting Started

## 1. Clone the Repository

Clone the repository from the `main` branch.
We assume you already know how to clone a Git repository.
If not, there are plenty of sources out there to learn how to do this.

---

## 2. Open the Project in Unity

Open the project using:
```text
Unity 2022.3.62f2
```

When the project opens, a welcome message will appear with additional setup instructions.
Follow the instructions shown in Unity before continuing.
This includes importing the required game DLLs from your Big Ambitions installation.

<img width="506" height="538" alt="{8601F8E0-E93E-492E-8BD7-88B268C61257}" src="https://github.com/user-attachments/assets/29ca51e0-09c4-4aaa-a7fb-c58e96f41ca7" />

---

## 3. Create Your Mod

Create your mod inside Unity.
The project includes example mods that demonstrate the expected structure, workflows, and APIs.
Make sure you have the mod manifest scriptable object filled with the information of your mod including localization, dependencies, asset bundles and more.

---

## 4. Build and Install

Use the **Build & Install** option in the **Mod builder** tool.
You can find the tool through the welcome page or the **Big Ambitions/Mod Builder** in the top level menu. 

<img width="529" height="80" alt="{1856093D-0BF5-4DE1-8CCB-64BC33F861DB}" src="https://github.com/user-attachments/assets/60120e95-a767-45d2-8e77-135c6d9145fe" />


This will:
1. Validate the mod and let you know if any problems are found
2. Compile your mod
3. Automatically install it into your local Big Ambitions mod folder

Your mod will be installed into:
```text
ModsLocal
```

This is the same folder that opens when clicking **Browse mod folder** inside the in-game Mod Creator window.

<img width="505" height="120" alt="{3F475614-FE72-44F2-9CA5-94F4325912C4}" src="https://github.com/user-attachments/assets/ada42456-44ca-43cf-b78d-811725ec8e1b" />

---

## 5. Upload Your Mod In Game

Launch **Big Ambitions** through Steam.

From the main menu:
```text
Mods > Mod Creator > Create new mod (or "Edit mod" on one of your uploaded mods)
```

Then:
1. Click **Browse mod folder**
2. Confirm your mod exists inside the `ModsLocal` folder
3. Select your local mod in the dropdown, make sure your thumbnail is inside the root of your mod folder
4. Fill out the mod information form
5. Upload your mod

<img width="332" height="322" alt="{082D5850-90DB-4814-A14F-134C1AADD750}" src="https://github.com/user-attachments/assets/8c7ffa98-fce3-4981-8c6f-1f70427f7d97" />

---

# Troubleshooting

## Scripts fail to compile

Make sure you completed the DLL import steps from the Unity welcome message.
The required Big Ambitions DLLs must be imported before the project can compile successfully.
After updating Big Ambitions on Steam, you'll have to reimport the DLLs to get the new versions of the lastest update.

---

## The mod does not appear in the game

Verify that the mod was installed into the `ModsLocal` folder.
You can open this folder from inside the game:
```text
Mods > Mod Creator > Browse mod folder
```

---

## Upload fails

Make sure:
- Your mod exists inside `ModsLocal`
- All required fields in the Mod Creator form are filled out
- The build completed successfully
- The thumbnail isn't too big (max 1MB due to Steam limitation)

# Notes
This is a public community repository. Custom Unity tools that are helpful to the creation of mods are welcomed through pull requests.

## Links

[![Steam](https://img.shields.io/badge/Steam-Big%20Ambitions-000000?style=for-the-badge&logo=steam&logoColor=white)](https://store.steampowered.com/app/1331550/Big_Ambitions/)

[![Discord](https://img.shields.io/badge/Discord-Join%20Community-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/44aGAt4Sv4)

[![Forum](https://img.shields.io/badge/Forum-Community-2ea44f?style=for-the-badge)](https://community.bigambitionsgame.com/)

[![Reddit](https://img.shields.io/badge/Reddit-r%2Fbigambitions-FF4500?style=for-the-badge&logo=reddit&logoColor=white)](https://www.reddit.com/r/bigambitions/)

[![YouTube](https://img.shields.io/badge/YouTube-Hovgaard%20Games-FF0000?style=for-the-badge&logo=youtube&logoColor=white)](https://www.youtube.com/channel/UCw5PR6gdYaWfZ0-d0WNJu1g)

[![X](https://img.shields.io/badge/X-Hovgaard%20Games-000000?style=for-the-badge&logo=x&logoColor=white)](https://twitter.com/hovgaardgames)
