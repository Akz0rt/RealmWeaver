# Дизайн: Region-лейблы (LOD-оверлей + редактируемые сохраняемые названия)

Дата: 2026-07-09
Статус: утверждён в брейншторме, готов к плану
Эталон: `design_handoff_realmweaver_map/` (Terra Umbrarum), раздел «Labels & Map Chrome» + `screens/01-master-view.png`

## Контекст

В приложении карта рисуется GPU-шейдером из per-cell `BiomeFamily` (11 семейств: Sea, Lake, Coast,
Snow, Tundra, Highland, Badlands, Forest, ForestWarm, Moor, Plains). Реального именования регионов
пока нет — `PoiToolPanel.RegionLabel` даёт позиционную заглушку. Хэндофф задаёт **латинские
флейвор-имена по типу биома** в центроидах (SILVA UMBRARUM, VASTA CINERIS…), курсивный засечный
шрифт, тёмное гало, избегание коллизий.

Этот кусок — **редактируемые сохраняемые названия регионов** с LOD по зуму. Последовательность
sub-project C: iso-декорации (в релизе) → POI-медальоны (в релизе) → **region-лейблы (эта спека)** →
хром (компас/масштаб/картуш/рамка) → fog of war.

## Ключевые решения (из брейншторма)

1. **Экранный оверлей + LOD по зуму** (uGUI ScreenSpaceOverlay): лейбл видим когда карта отдалена,
   плавно гаснет при приближении (как подписи регионов в Genshin). Вторая половина LOD (POI-подписи
   проявляются вблизи) — **вне области**, отдельно.
2. **Гранулярность — на крупный связный участок**: flood-fill по графу соседства клеток, группируя
   смежные клетки одного семейства; лейбл на каждый участок с площадью ≥ порога.
3. **Латинские авто-имена по `BiomeFamily`** (таблица ниже) как значения по умолчанию.
4. **Полный CRUD-редактор + персистентность**: лейблы авто-сидятся при генерации, дальше это
   **сохраняемые редактируемые объекты** — переименовать / перетащить / удалить / добавить новый;
   сохраняются в `.dndproj`.
5. **Тумблер** «Названия регионов» в панели слоёв (по умолчанию ON).
6. **Шрифт** — засечный курсив (IM Fell English, OFL) через **TextMeshPro** (letter-spacing + гало).
7. **Избегание коллизий — базовое (v1)**: центроид + лёгкий вертикальный сдвиг при наложении на
   другой лейбл или POI-медальон.

## Модель данных

```csharp
[Serializable]
public class RegionLabelData
{
    public string Id;                              // guid, стабильный ключ
    public string Text;                            // отображаемое имя (латинское по умолчанию, DM правит)
    public System.Numerics.Vector2 WorldPosition;  // мировой центроид/точка привязки (XZ карты)
    public BiomeFamily SeedFamily;                 // семейство, из которого засижен (справочно/будущий цвет)
}
```

Новое поле `List<RegionLabelData> RegionLabels` в `ProjectSaveData`/`ProjectLoadResult`. **Аддитивно**:
старые `.dndproj` без поля → Newtonsoft даёт `null` → `Load` подставляет пустой список (как `Cells`/`Pois`).
Версию формата не бампим, миграции нет. `BiomeFamily` сериализуется как **int** (Newtonsoft без
`StringEnumConverter`) — enum стабильный (11 значений, менять только append-only); `SeedFamily` —
справочное поле, не критично к точности.

## Компоненты (развязанные, как в POI/декорациях)

### 1. `RegionLabelPlacer` (чистый C#, тестируемый)
`Assets/WorldGen/Rendering/RegionLabels/RegionLabelPlacer.cs`

- Вход: `IReadOnlyList<VoronoiCell> cells`. Семейство клетки — через **`RegionCategories.FamilyCategoryOf(c)`**
  (тот же источник, что рендер: индекс семейства суши, −1 для воды) → лейблы совпадают с раскраской.
- **Flood-fill связных компонент**: обход по `cell.NeighborIds`, группируя смежные клетки с одинаковым
  `FamilyCategoryOf ≥ 0` (только суша). Каждая компонента = участок биома.
- Для каждой компоненты с числом клеток ≥ `minPatchCells` (дефолт 6): **центроид** = среднее
  `cell.Site`, взвешенное по площади клетки (площадь полигона), либо простое среднее `Site` если
  полигонов нет. → `new RegionLabelData { Id=new guid, Text=LatinName(family), WorldPosition=centroid,
  SeedFamily=family }`.
- **Именованные семейства только**: `LatinName(family)` определена для Forest/ForestWarm/Badlands/
  Plains/Highland/Snow/Moor/Tundra; для `Coast` (и любого не в таблице) возвращает `null` → такой
  участок **пропускаем** (тонкая кромка не подписывается). Море обрабатывается отдельно ниже.
- **Море**: 1–2 морских лейбла (OCEANUS UMBRAE / MARE GELIDUM) в крупнейших открытых участках океана
  (центроид самой большой водной компоненты слева и, если хватает места, справа/снизу от континента;
  простая эвристика по bbox воды). Опционально; на маленькой воде — один.
- Детерминировано (без `Random`). Возвращает `List<RegionLabelData>`.

Таблица имён `BiomeFamily → латынь` (значения по умолчанию, DM правит через CRUD):

| Семейство | Имя | Семейство | Имя |
|---|---|---|---|
| Forest | SILVA UMBRARUM | Highland | DORSUM CORVI |
| ForestWarm | SILVA IGNEA | Snow | NIX AETERNA |
| Badlands | VASTA CINERIS | Moor | PALUS NIGRA |
| Plains | CAMPI CANI | Tundra | GLACIES |
| Sea | OCEANUS UMBRAE / MARE GELIDUM | (Coast, Lake) | не подписываются |

### 2. `RegionLabelManager` (MonoBehaviour) — как облегчённый `PoiManager`
`Assets/WorldGen/Rendering/RegionLabels/RegionLabelManager.cs`

- Держит `List<RegionLabelData> labels`; события `OnLabelsChanged`, `OnSelectionChanged(RegionLabelData)`.
- CRUD: `AddLabel(worldPos, text) → id`, `DeleteLabel(id)`, `RenameLabel(id, text)`, `MoveLabel(id, worldPos)`,
  `SelectLabel(id)` / `DeselectAll()` / `GetSelected()`.
- `SeedFromCells(cells)` — гоняет `RegionLabelPlacer`, **заменяет** список (свежий сид на новую генерацию).
- `LoadLabels(list)` / `ClearAll()` / `GetAll()`.
- Ссылка на `MapCameraController` (для LOD в оверлее) и `mapRenderer` (мир↔экран, привязка к плоскости карты).
- Self-tests: сид (число ≥1 на фикстуре), CRUD (add/rename/move/delete меняют список), select.

### 3. `RegionLabelOverlay` (MonoBehaviour, uGUI ScreenSpaceOverlay) — рендер + LOD + правки
`Assets/WorldGen/Rendering/RegionLabels/RegionLabelOverlay.cs`

- Свой `Canvas` (ScreenSpaceOverlay) + `GraphicRaycaster`, как `PoiEditPanel`/`PoiToolPanel`.
- На каждый лейбл — **TMP `TextMeshProUGUI`** (засечный курсив, заглавные, letter-spacing, тёмная
  обводка/underlay = гало) + прозрачная кнопка (клик → `SelectLabel`).
- `LateUpdate`: для каждого лейбла `Camera.WorldToScreenPoint(world(WorldPosition))` → `anchoredPosition`;
  **альфа по LOD** (см. ниже); скрыть если `z<0` (за камерой) или за краями экрана; **базовый сдвиг**
  при коллизии (сорт по Y, вертикальный офсет vs уже размещённых прямоугольников лейблов + экранных
  прямоугольников POI-маркеров). Размер экранно-постоянный.
- **Выделение**: у выбранного лейбла — инлайн TMP `TMP_InputField` (переименование, `onEndEdit → RenameLabel`)
  + маленький «×» (удаление). **Перетаскивание** (drag выбранного) → анпроджект экран→плоскость карты
  (луч из камеры в XZ-плоскость `y=0`) → `MoveLabel`.
- **Режим добавления**: тумблер/кнопка «+ Название региона» → следующий клик по карте (не над UI) создаёт
  лейбл в той мировой точке (`AddLabel`, дефолтный текст напр. «NOVA REGIO») и выделяет для правки.
- `SetVisible(bool)` — для тумблера слоёв.

### 4. Интеграция
- **Сид/загрузка**: `MapScreenController`/`WorldMapRenderer` — `SeedFromCells` на новой генерации;
  `LoadLabels` при загрузке проекта; на brush-правках **не трогаем** (лейблы уже пользовательские).
  Действие **«Пересоздать названия из биомов»** (кнопка) — ручной ре-сид (сброс к авто).
- **Тумблер** «Названия регионов» в `MapLayersPanel` → `overlay.SetVisible` (дефолт ON).
- **Ввод не конфликтует**: клики по uGUI-лейблам ловит `GraphicRaycaster`; пан/зум/кисть уже пропускают
  ввод при `EventSystem.IsPointerOverGameObject()` (существующий паттерн). Клик режима добавления по
  карте (не над UI) обрабатывает инструмент.

### 5. Персистентность
`ProjectSerializer.Save/Load` + `ProjectSaveData` получают `RegionLabels`. Round-trip self-test
(`RegionLabelData` с кастомным текстом + позицией + scale). Старые `.dndproj` грузятся с пустым списком.

### 6. Шрифт / TMP
Бандлим **IM Fell English** (OFL, Google Fonts) `.ttf` → **TMP Font Asset** (латиница заглавные + базовое).
Разовый «Import TMP Essentials». Это Editor-шаг юзера (укажем в плане: положить `.ttf` в `Assets/Fonts/`,
создать TMP-ассет, назначить в оверлей). Латиница-only → ассет крошечный. Шов замены шрифта остаётся.

## LOD (фейд по зуму)

Мировой размер маркеров у нас уже привязан к зуму (`MapCameraController.NaturalFitSize`,
`targetCamera.orthographicSize`). Лейблы: **альфа** = `smoothstep(nearFrac, farFrac, orthoSize/NaturalFitSize)`:
полностью видно при `orthoSize ≥ farFrac·fit` (карта отдалена), гаснет к 0 при `orthoSize ≤ nearFrac·fit`
(зум внутрь). Дефолты `farFrac=0.8`, `nearFrac=0.35`, серилизуются (крутятся в Инспекторе). Размер
экранно-постоянный (без масштабирования шрифта под зум) — только альфа.

## Тестирование / персистентность

Агенты не запускают Unity → `[ContextMenu]`/self-tests гоняет юзер; визуал — юзер в Editor.

- **`RegionLabelPlacer`** (self-test): фикстура клеток с 2–3 известными участками разных семейств →
  верное число компонент, центроид внутри участка, имена по таблице, порог `minPatchCells` отсекает мелочь.
- **`RegionLabelManager`** (self-test): `SeedFromCells` даёт ≥1 лейбл; `AddLabel/RenameLabel/MoveLabel/
  DeleteLabel` корректно меняют список; select/deselect.
- **`ProjectSerializerSelfTests`** (расширить): round-trip проекта с `RegionLabels` (кастомный текст +
  позиция) — грузится идентично; старый JSON без поля → пустой список.
- **Визуал (юзер, Editor)**: лейблы стоят на верных биомах латинскими именами; переименование (клик→поле),
  перетаскивание, удаление, добавление работают и **сохраняются** через save/load; LOD — видно отдалённо,
  гаснет при приближении; курсив+гало читаемы; на редкой карте нет наложений; тумблер прячет/показывает;
  «Пересоздать из биомов» сбрасывает к авто; кисть-правки лейблы не двигают.

## Точки касания

- Новые: `RegionLabels/RegionLabelPlacer.cs`, `RegionLabelManager.cs`, `RegionLabelOverlay.cs`,
  `RegionLabelData.cs` (+ .meta), TMP Font Asset + `.ttf`.
- Правки: `Persistence/ProjectSerializer.cs` + `ProjectSaveData` (+ `RegionLabels`),
  `ProjectSerializerSelfTests.cs`; `MapLayersPanel.cs` (тумблер); `MapScreenController.cs`/
  `WorldMapRenderer.cs` (сид на ген/загрузку + «пересоздать»).
- Переиспользуем: `RegionCategories.FamilyCategoryOf`, `MapPalette.GetFamily`, `MapCameraController`
  (LOD), `VoronoiCell.NeighborIds/Site/Polygon`.
- НЕ трогаем: рендер карты (шейдер/label-текстура), POI-систему, формат клеток.

## Вне области (отдельные куски)

- Вторая половина LOD — POI-подписи проявляются при приближении (перегейтить существующий `showPoiLabels`).
- Хром: компас/масштаб/картуш/рамка/виньетка.
- Редактирование границ регионов (полигоны) — это точечные name-лейблы, не редактор областей.
- Авто-переустановка лейблов после каждой brush-правки (лейблы пользовательские после сида; вместо этого
  ручное «Пересоздать»).
- 4 палитры/цвет лейбла под тему — пока фикс. светлый + гало.

## Риски

- **Сериализация enum** (`BiomeFamily` как int в `SeedFamily`; проект кусало 3× на serialization):
  `BiomeFamily` — append-only; `SeedFamily` справочное, не критично. Round-trip обязателен.
- **TMP-ассет** (Editor-шаг юзера): латиница-only → мелкий; забытый ассет = пустой текст — проверить
  назначение в оверлее.
- **Дизамбигуация ввода** (клик по лейблу vs POI vs кисть/пан): полагаемся на `IsPointerOverGameObject`
  (существующий паттерн) — юзер верифицирует, что правка лейбла не красит/не таскает карту.
- **Центроид вогнутого участка** может лечь на «дыру»/воду — приемлемо для v1 (DM подвинет; CRUD есть).
- **LOD-пороги** — ощущение подбирается; тюнинг-поля + визуальная проверка юзером.
- **Плотность лейблов** при мелком `minPatchCells` — базовый сдвиг может не спасти; юзер оценит, порог крутится.
