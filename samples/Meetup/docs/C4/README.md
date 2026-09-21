' C4 model diagrams for the Meetup sample (diagram-as-text).
' Render with PlantUML + C4-PlantUML:
'   https://github.com/plantuml-stdlib/C4-PlantUML
' Each level has its own .puml file in this folder.

' Levels:
'   c1_system_context.puml  - C1: users + Meetup system + external dependencies
'   c2_container.puml       - C2: the single deployable + per-module databases
'   c3_components.puml      - C3: host + five modules + in-process event bus
'   c3_components_module.puml - C3: inside one module (Meetings shown)
'   c4_class.puml           - Code: Meeting aggregate
