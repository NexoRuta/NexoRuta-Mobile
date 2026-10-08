# NexoRuta Mobile

Aplicación móvil del proyecto NexoRuta, creada con .NET MAUI como punto de partida para el cliente de repartidores. El repositorio contiene una sola app multiplataforma; todavía conserva la pantalla de muestra de MAUI y no implementa operaciones de reparto ni conexión con la API.

## Estado actual

La solución compila para los destinos configurados con los workloads disponibles en el entorno de desarrollo. Esto no demuestra que la aplicación se haya instalado o ejecutado en un teléfono, emulador, iOS o MacCatalyst. **La ejecución en dispositivos y la integración con backend no están validadas.** Tampoco se afirma que haya autenticación, modo sin conexión, sincronización o CI. Esta solución no contiene pruebas automatizadas: **se ejecutan cero pruebas**.

## Estructura

```text
NexoRuta.sln
src/NexoRuta.Mobile/
  NexoRuta.Mobile.csproj  # app MAUI single-project y frameworks condicionales
  MauiProgram.cs         # composición de la app
  App.xaml, AppShell.xaml
  MainPage.xaml(.cs)     # pantalla de contador de la plantilla
  Platforms/             # arranque y manifiestos por plataforma
  Resources/             # icono, splash, estilos y recursos
```

El proyecto configura `net10.0-android`; agrega iOS y MacCatalyst cuando el host no es Linux, y Windows únicamente en Windows. La plataforma destino y sus herramientas disponibles determinan qué framework puede compilarse o ejecutarse.

## Requisitos

- .NET SDK 10 y workloads de .NET MAUI para el destino elegido. En el checkout combinado, `global.json` de la raíz selecciona SDK `10.0.401`; el submódulo aislado no trae ese selector.
- Android: Android SDK y emulador/dispositivo configurados para ejecutar.
- iOS/MacCatalyst: macOS y herramientas Apple/Xcode compatibles.
- Windows: Windows y el workload/SDK de Windows correspondiente.

## Compilar y comprobar

Desde la raíz de este repositorio:

```bash
dotnet restore NexoRuta.sln --nologo
dotnet build NexoRuta.sln --no-restore --nologo
dotnet test NexoRuta.sln --no-build --no-restore --nologo
```

Verificado el 7 de octubre de 2026: restore y build finalizaron sin advertencias ni errores para los destinos configurados en el entorno. `dotnet test` terminó con código 0, pero no encontró pruebas ejecutables: **0 pruebas ejecutadas**. La compilación no es evidencia de ejecución en hardware.

## Ejecutar

Para iniciar la app en un destino Android ya configurado, el comando de desarrollo es:

```bash
dotnet build src/NexoRuta.Mobile/NexoRuta.Mobile.csproj -t:Run -f net10.0-android
```

Este comando requiere un emulador iniciado o dispositivo Android conectado y reconocido por las herramientas locales. La instalación/ejecución en un dispositivo no fue parte de la validación registrada para este repositorio; no se presenta como comprobada. Para iOS, MacCatalyst o Windows hay que elegir el framework correspondiente al host y disponer de sus herramientas nativas. Esta app no expone una URL ni un puerto HTTP.

## Pendiente

Reemplazar la pantalla de contador por flujos del repartidor, definir contratos de API, implementar autenticación y sincronización segura (incluido el trabajo sin conexión si corresponde), y agregar pruebas. Cada uno de esos puntos requiere validación funcional independiente.
