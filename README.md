<p align="center">
  <img alt="Space Station 14" width="500" height="145" src="https://github.com/user-attachments/assets/082b7cc8-5930-40ff-8bfa-9c2d36f6d1fb" />
</p>

<p align="center">
  Невероятный симулятор историй!<br>
  Основан на идеях <a href="https://github.com/Twilight-Fortress-SS13/Twilight-Axis">Twilight-Axis</a> и <a href="https://github.com/Azure-Peak/Azure-Peak">Azur-Peak</a> из Space Station 13.
</p>

<div align="center">

  [![Steam](https://img.shields.io/badge/Steam-Скачать-blue?style=for-the-badge)](https://store.steampowered.com/app/1255460/Space_Station_14/)

</div>

---

**Amberfall** — это самостоятельная сборка Space Station 14, стремящаяся воссоздать атмосферу тёмного фэнтези.

*Special thanks to:* [Twilight-Axis](https://github.com/Twilight-Fortress-SS13/Twilight-Axis), [Azure-Peak](https://github.com/Azure-Peak/Azure-Peak), [Trauma-Station](https://github.com/Trauma-Station/Trauma-Station) & [Goob-Station](https://github.com/Goob-Station/Goob-Station), [WizDen Space Station 14](https://github.com/space-wizards/space-station-14.git).

---
<div align="center">

## Контрибуция

</div>

Мы всегда рады помощи в разработке, если вы хотите внести свой вклад, присоединяйтесь к [серверу разработки в Discord](https://discord.gg/arcane-ss14). Вы можете помочь нам, решая проблемы из [списка открытых проблем](https://github.com/ArcaneSS14/amberfall/issues) или предлагая свои идеи. Не стесняйтесь задавать вопросы — мы всегда готовы помочь!

---
<div align="center">

## Сборка

</div>

</div>

### Windows

> 1. Клонируйте данный репозиторий.
```shell
git clone https://github.com/ArcaneSS14/amberfall.git
```
> 2. Откройте коммандную строку в папке репозитория и введите команду для того, чтобы скачать движок игры.
```shell
git submodule update --init --recursive
```
> 3. Следующим этапом идёт билд-билда, для этого нужно ввести команду с указанием того, для чего вы билдите, для этого нужно написать Release, Tools или Debug.
```shell
dotnet build --configuration Release/Tools/Debug
```
> [!TIP]
> К примеру **Release** - полная версия, **Tools** - урезаная версия, **Debug** - урезаная версия, но которая будет вылетать при любой ошибке. В большинстве случаев вам хватит **Tools**, что-бы не перенапрягать машину.

> 4. Далее вам требуется запустить сервер с клиентом, для этого есть несколько способов.
> - 4.1. Командами, в конце так же можно указать вместо Tools любой интересующий вас тип.
```shell
dotnet run --project Content.Server --configuration Tools
```
```shell
dotnet run --project Content.Client --configuration Tools
```
> - 4.2. Запуск .bat файла, который автоматически выполнит те же команды.
```shell
Scripts/bat/runQuickAll.bat
```
> 5. Подключитесь к **localhost** в появившемся окне и играйте!

---
<div align="center">

## Лицензия

</div>

All code in this codebase is released under the [AGPL-3.0](LICENSE-AGPLv3.TXT)-or-later license. Each file includes REUSE Specification headers or separate .license files that specify a dual license option. This dual licensing is provided to simplify the process for projects that are not using AGPL, allowing them to adopt the relevant portions of the code under an alternative license.

Most media assets are licensed under [CC-BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/) unless stated otherwise. Assets have their license and the copyright in the metadata file. [Example](https://github.com/space-wizards/space-station-14/blob/master/Resources/Textures/Objects/Tools/crowbar.rsi/meta.json).

By submitting a pull request or making a commit to the Arcane Station, you agree to the terms of our [Contributor License Agreement](LICENSE-CLA.TXT). This agreement grants us the right to distribute your contributions under any license we choose, while you retain your copyright ownership.

</div>

> [!NOTE]
> Some assets are licensed under the non-commercial [CC-BY-NC-SA 3.0](https://creativecommons.org/licenses/by-nc-sa/3.0/) or similar non-commercial licenses and will need to be removed if you wish to use this project commercially.
