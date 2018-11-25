# Leanware Project

This week's assignment continues the previous one and adds a new use case.

## Features arrangement by priority

Features appear on UI as several lists (one list for each status). Each list shows features sorted by priority, which is know also as stack rank of a feature (its position in the list). Features can be re-arranged with drag-n-drop, moving from position to position, which should be updated and saved on backend, so that later features can be retrieved and shown in proper order.

`GET /api/features/stack/{status:string}`
Returns features with given status sorted by their stack rank (priority)

`POST /api/features/{id}/position/{position:int}`
Moves given feature to the given position in the list of other features with the same status.