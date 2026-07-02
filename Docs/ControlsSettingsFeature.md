# Controls Settings (перепривязка управления)

Пользовательская перепривязка клавиш поверх Unity Input System с сохранением в PlayerPrefs
(JSON), с отдельной страницей в меню настроек. На первом этапе меняются только
клавиатура/мышь; геймпад показывается «только для чтения».

## Хранилище настроек (`Assets/_Scripts/UI/Settings/ControlBindingsStore.cs`)

Настройки сериализуются штатным механизмом Input System — **биндинг-оверрайдами**:
`InputActionAsset.SaveBindingOverridesAsJson()` даёт компактный JSON только изменённых
пользователем биндингов, а `LoadBindingOverridesFromJson(json)` их накладывает. Это и есть
«сериализуемые кастомные настройки».

- `IControlSettingsStore` — абстракция хранилища (`HasData`/`Load`/`Save`/`Clear`).
  Позволяет позже перенести JSON куда угодно (файл, облако, бэкенд), подменив реализацию.
- `PlayerPrefsControlSettingsStore` — реализация по умолчанию, ключ `controls.bindings.v1`.
- `ControlSettings` — статический фасад:
  - `Apply(asset)` — загрузить сохранённый JSON и наложить на ассет;
  - `Save(asset)` — сериализовать текущие оверрайды и записать в хранилище;
  - `ResetAll(asset)` — снять все оверрайды и очистить хранилище;
  - `SetStore(store)` — подменить хранилище.

`ControlSettings.Apply` вызывается в двух местах, чтобы кастомные биндинги действовали в
обеих сценах:
- `EntryPoint.cs` (геймплей) — сразу после регистрации `InputActionsComponent`, до чтения инпута;
- `ControlsSettingsMenu.Awake` (сцена меню, где нет `EntryPoint`).

`ControlSettings.Save` вызывается после каждой успешной перепривязки.

## UI

### `ControlBindingRow.cs` — строка одного контрола
Вью для префаба строки: `UniText` имя контрола (напр. «Move Left»), `Button` +
`UniText` с основной клавишей/мышью (клик запускает перепривязку), `UniText`
`_keyboardAlternativeLabel` с альтернативной клавишей того же контрола (напр. `W` / `UpArrow`,
только показ), `UniText` с контролом геймпада (показ). Стики геймпада подписываются
вручную («Left Stick» / «Right Stick»), т.к. у них нет читаемого display-string.

Флоу перепривязки (`PerformInteractiveRebinding`):
1. клик по слоту → слот подсвечивается `_listeningColor`, надпись меняется на `_listeningText`
   («Press a key...»); одновременно «слушает» только одна строка (статический `s_active`);
2. игрок жмёт клавишу/кнопку мыши → биндинг применяется, `Refresh` обновляет надпись,
   вызывается колбэк сохранения (`ControlSettings.Save`);
3. `Esc` или повторный клик по слоту → отмена, слот возвращается к прежнему цвету/тексту.

Ввод ограничен клавиатурой/мышью через `WithControlsExcluding("<Gamepad>")` (+ исключение
`position`/`delta`/`scroll` мыши); отмена — `WithCancelingThrough("<Keyboard>/escape")`.
Поля подсветки (`_listeningTarget`, `_listeningColor`, `_listeningText`) опциональны:
если `_listeningTarget` не задан, берётся `targetGraphic` кнопки.

### `ControlsSettingsMenu.cs` — построение списка
Строит строки из `InputActionAsset` (карта по умолчанию — `Player`) в `Content` у
`ScrollRect` (Content должен нести `VerticalLayoutGroup`). Для каждого экшена **группирует**
клавиатурные/мышиные биндинги одного контрола в одну строку: первый — основной
(перепривязывается), второй — альтернативный (в колонке `_keyboardAlternativeLabel`),
третий+ игнорируется. Так убираются строки-дубликаты (напр. `W` и `UpArrow` для «Move Up»).
Ключ группировки: часть композита — по имени (up/down/left/right), одиночные биндинги — все в
одну группу экшена. Композитный заголовок пропускается. В колонку геймпада подставляется
соответствующий биндинг того же экшена: для части композита — часть с тем же именем, иначе
одиночный геймпадный биндинг (напр. `Move` → `leftStick` → «Left Stick»). Кнопка Reset вызывает
`ControlSettings.ResetAll` и перестраивает список.

## Что нужно собрать в Unity (редактор)

Скрипты не создают префабы/сцены — это делается в редакторе:

1. **Префаб строки** (`Assets/_Prefabs/UI/Settings/ControlBindingRow.prefab`): корень с
   `ControlBindingRow` + `LayoutElement`, внутри — `UniText` (имя), `Button` с дочерним
   `UniText` (основная клавиша), `UniText` (альтернативная клавиша), `UniText` (геймпад).
   Разложить горизонтально (напр. `HorizontalLayoutGroup`). Проставить ссылки в инспекторе строки
   (`_nameLabel`, `_keyboardButton`, `_keyboardLabel`, `_keyboardAlternativeLabel`, `_gamepadLabel`).
2. **Страница настроек управления**: `ScrollRect` → `Viewport` → `Content` с
   `VerticalLayoutGroup` (+ `ContentSizeFitter`, Vertical = Preferred Size). На объект с
   `ControlsSettingsMenu` назначить: `_actions` (тот же `InputSystem_Actions`, что в `EntryPoint`),
   `_scroll`, `_content` (= Content), `_rowPrefab`, опционально `_resetButton`.
3. Открыть страницу из `MainMenuController` (кнопка Settings уже ведёт на `_settingsPage`) —
   разместить `ControlsSettingsMenu` внутри Settings Page либо как отдельную под-страницу.

> Важно: `ControlsSettingsMenu._actions` должен ссылаться на тот же ассет
> `InputSystem_Actions`, что используется в геймплее, иначе оверрайды не совпадут.
