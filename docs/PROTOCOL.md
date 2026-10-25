# Протокол телефон ↔ ноут

- Обнаружение: телефон шлёт UDP `LAZYASF?` на broadcast:47801, ноут отвечает
  `{"app":"LazyASF","name":"<имя ПК>","port":47800}`.
- Управление: WebSocket `ws://<ip>:47800/ws`, текстовые JSON-сообщения с полем `t`.

## Вход
| телефон → ноут | ответ |
|---|---|
| `{"t":"pair","pin":"1234","device":"Xiaomi ..."}` | `{"t":"paired","token":"…","name":"…"}` или `{"t":"auth_failed"}` |
| `{"t":"hello","token":"…","device":"…"}` | `{"t":"welcome","name":"…"}` или `{"t":"auth_failed"}` |

После входа ноут сразу шлёт `state` и `devices`.

## Команды телефона
| `t` | поля | что делает |
|---|---|---|
| `move` | `dx`, `dy` (пиксели, дробные) | сдвиг курсора |
| `scroll` | `dy`, `dx` (120 = щелчок колеса) | прокрутка |
| `btn` | `b`: left/right/middle, `down`: bool | зажать/отпустить |
| `click` | `b` | клик |
| `key` | `k`: `space`, `f11`, `ctrl+w`, `volup`, `playpause`, `browserback`… | клавиша/сочетание |
| `text` | `s` | ввод текста (Unicode) |
| `launch` | `site`: youtube/boosty/kino, `headphones`, `output`: headphones/tv | режим ТВ |
| `audio_output` | `target`: headphones/tv, `headphones` | переключить звук на лету |
| `headphones_connect` | `headphones` | только переподключить наушники |
| `restore` | — | вернуть всё как было |
| `sleep` | `minutes` | таймер сна |
| `sleep_cancel` | — | отменить таймер |
| `devices` | — | список Bluetooth-наушников |
| `ping` | — | `pong` |

## Сообщения ноута
- `{"t":"state","state":{"active","busy","site","headphones","headphonesConnected","sleepEndsAt","output","steps":[{"id","title","status","detail"}]}}`
  — `status`: pending | running | ok | fail | skip.
- `{"t":"devices","devices":["REDMI Buds 8 Pro", …],"default":"REDMI Buds 8 Pro"}`
