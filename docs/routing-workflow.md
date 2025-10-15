# Routing check/update workflow

This repository surfaces the routing status inside the camera dashboard. The routing logs and cards shown in the UI ("Routing Check" / "Routing Update") are updated from the Blazor page code-behind in `BlazorApp5/Components/Pages/Index.razor`.

## UI event handlers

* **`PerformRoutingCheck()`** – Builds the payload for the `ROUTING_CHECK` request, calls the MES endpoint, and rewrites `State.RoutingCheckLog` with the latest request/response body so the "Routing Check" card matches the screenshot. The guard at the top of the method prevents the call when no work order is selected.
* **`PerformRoutingUpdate()`** – Mirrors the check flow for the `ROUTING_UPDATE` call and keeps `State.RoutingUpdateLog` in sync with the latest MES response.

Both handlers live around lines 1640–1788 of `Index.razor` and are invoked when scans request routing or when the manual **Routing Test** button is pressed.

## Service layer

* **`RoutingCheckService.UpdateRoutingAsync(...)`** – Lower-level helper that assembles the routing JSON and posts it to the MES API. The UI path above inlines similar logic today, but this service remains available for background jobs.
* **`RoutingCheckService.CheckRoutingAsync(...)`** – Wraps the GET-based routing check endpoint and is used wherever only the pass/fail status string is needed.

You can find these service methods in `BlazorApp5/Services/RoutingCheckService.cs`.

