# Evidencias

Capturas de las pruebas obligatorias (sección 6 del laboratorio), tomadas sobre el entorno local de desarrollo con los usuarios de prueba.

| # | Prueba | Resultado esperado | Captura |
|---|---|---|---|
| 1 | Registrar un cliente con dos vehículos | Cliente y 2 vehículos guardados | [01-cliente-dos-vehiculos.png](01-cliente-dos-vehiculos.png) |
| 2 | Crear una orden de reparación | Orden en estado Recibido | [02-orden-recibido.png](02-orden-recibido.png) |
| 3 | Asignar un mecánico | Orden con mecánico asignado | [03-mecanico-asignado.png](03-mecanico-asignado.png) |
| 4 | Modificar una orden sin permisos (usuario Cliente, Swagger) | Error 403 | [04-modificar-sin-permiso-403.png](04-modificar-sin-permiso-403.png) |
| 5 | Crear un presupuesto | Presupuesto con subtotales y total | [05-presupuesto-subtotales-total.png](05-presupuesto-subtotales-total.png) |
| 6 | Aprobar presupuesto con usuario sin el claim `Ordenes.Aprobar` | Error 403 con Correlation ID | [06b-aprobar-sin-claim-403.png](06b-aprobar-sin-claim-403.png) |
| 6 | Aprobar el presupuesto con el usuario autorizado | Estado «Aprobado» | Pendiente |
| 7 | Registrar fotografías de los daños | Imágenes guardadas y rutas en la base | Pendiente |
| 8 | Descontar repuestos del inventario | Existencias actualizadas + movimiento | Pendiente |
| 9 | Descontar más repuestos de los disponibles | Error claro; existencias sin cambios | Pendiente |
| 10 | Dos operaciones simultáneas (concurrencia) | Una se aplica; la otra recibe 409 | Pendiente (ejecutar `dotnet run --project AutoFix.PruebaConcurrencia`) |
| 11 | Acceder a la API sin JWT | Error 401 | Pendiente |
| 12 | Consultar los logs de una operación fallida | Log con usuario, endpoint, código y Correlation ID | Pendiente |
