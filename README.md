Создает карточки для Anki с переводом слов на карточках MTG

Запускать через MtgTeacher.Cli

`dotnet run -- --card-list-file <mtglist>`

## Требования

Добавить в `MtgTeacher.Cli/appsettings.json` ключи для апи яндекса. 

Яндекс словарь (YANDEX_DICT_KEY): https://yandex.com/dev/dictionary/keys/get/

Яндекс переводчик (YANDEX_TRANSLATE_KEY и YANDEX_FOLDER): https://cloud.yandex.ru/docs/translate/quickstart

## Roadmap

* Перейти на MS Cognitive services и добавить чтение транслитирации
* Лучше отформатировать доп данные карточки что бы https://ankiweb.net/shared/info/2042118948 мог лучше их печатать

