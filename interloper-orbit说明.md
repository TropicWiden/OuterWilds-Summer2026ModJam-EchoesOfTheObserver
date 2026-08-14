# 闯入者轨道参数说明

模组在场景6开始时读取安装目录中的 `interloper-orbit.json`。修改后重新进入太阳系即可，不必重新编译 DLL。

## 主要参数

- `startDistanceFromSunMeters`：期望的开局距离，默认 120000 米。程序会反向推演一条向太阳飞行的轨道，使闯入者在指定时刻经过白洞站。为了保证始终向内飞行，极端的小数值可能会被自动提高。
- `interceptTimeMinutes`：经过白洞站的循环时刻，当前为第 15 分钟。
- `targetPredictionLeadSeconds`：白洞站位置的时间修正。实测如果提前经过可填正数，滞后经过可填负数。
- `interceptCenterDistanceMeters`：第15分钟时，闯入者轨迹中心与白洞站中心的距离。当前固定为 800 米，方向为沿太阳指向白洞站的方向向外。
- `sunInitialRadiusMeters`：太阳初始半径，当前为 2000 米。
- `solarSurfaceClearanceMeters`：闯入者理论近太阳点距离太阳表面的高度，当前为 800 米。因此轨迹近点距离太阳中心为 2800 米。

## 太阳状态

场景6中，太阳的自然老化、变色与膨胀会被冻结在循环开始时的状态。本体发出超新星触发事件后，太阳控制器会恢复运行，因此最后的坍缩、超新星和循环结算仍然保留。

## 复活截止点

程序会根据本次实际生成的闯入者轨道计算近太阳点时刻。闯入者到达距太阳中心 2800 米的近点时，玩家会被判定为在时间循环外死亡并返回主菜单。选择“加载之前的存档”后，模组会直接恢复场景6并让玩家出生在碎空星。

挪麦电脑的分钟数同样读取这个动态计算结果，不再使用本体固定的22分钟循环剩余时间。
- `interceptOffsetTargetLocalX/Y/Z`：交会点相对白洞站的局部偏移，单位为米。正式制作黑洞入口时可用它瞄准入口而不是白洞站本体。

## 地图轨迹

原版闯入者的地图椭圆会被关闭，改为根据闯入者当前真实位置、速度和太阳引力实时预测的开放轨迹。

- `mapTrajectoryPreviewMinutes`：地图从当前时刻向未来预测多少分钟。
- `mapTrajectoryPointCount`：轨迹线采样点数量。
- `mapRefreshIntervalSeconds`：地图轨迹刷新间隔。
- `mapSimulationStepSeconds`：地图预测的模拟步长。
- `mapMinimumSolarRadiusMeters`：轨迹异常进入太阳内部时停止预测的保护半径。当前为 1500 米；正常的相切轨迹不会触发它。

## 精度参数

- `simulationStepSeconds`：实际轨道反向推演步长。默认 0.25 秒。
- `distanceSolverIterations`：为了匹配期望起始距离而进行的计算次数。
- `maximumInterceptRadialSpeedMetersPerSecond`：求解的初始速度搜索上限；通常无需修改。

## 配置文件位置

游戏实际读取：

`C:\Users\danwe\AppData\Roaming\OuterWildsModManager\OWML\Mods\Known-Mouse.Return\interloper-orbit.json`

Visual Studio 项目中的同名文件只会在编译/安装时复制过去。直接测试参数时，请修改游戏实际读取的这一份。

## 日志判定

成功时会看到：

```text
[RETURN INTERLOPER] Applied inbound trajectory...
[RETURN INTERLOPER MAP] Replaced the cached vanilla ellipse with a live predicted trajectory.
```

第一条日志中的 `radial speed` 必须为负数，负数代表闯入者开局正在靠近太阳。
