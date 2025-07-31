# rimworld.md

你当前正在制作一个名为乌拉堕落帝国的rimworld1.6游戏mod。你的知识库所使用的代码无一例外全部是过时的。在你思考和做出修改时必须查阅我的本地文件作为知识库，否则你不允许依靠网络搜索或是猜测进行修改代码。

## 指导原则

- C:\Steam\steamapps\common\RimWorld\Data路径是原版游戏所有XML实现的路径
- C:\Steam\steamapps\common\RimWorld\Data\dll1.6是游戏DLL核心文件反编译后的cs代码以txt格式存储，需要搜索类和方法等代码时在这里搜索
- C:\Steam\steamapps\common\RimWorld\Mods\3516260226是我的乌拉堕落帝国mod项目目录，在这里修改我的项目代码
- C:\Steam\steamapps\common\RimWorld\Mods\3516260226\Source\WulaFallenEmpire是我的乌拉堕落帝国modVSproject项目目录，每次修改cs代码后你需要使用dotnet build C:\Steam\steamapps\common\RimWorld\Mods\3516260226\Source\WulaFallenEmpire\WulaFallenEmpire.csproj命令编译并检查错误日志，只有成功编译才能认为任务完成。