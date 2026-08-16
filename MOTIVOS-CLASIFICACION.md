# Clasificación motivo → origen · VALIDADO

Los 90 motivos de `Motivos.xlsx` son el catálogo **general** del sistema contable. Cada motivo
declara su **origen de libro**, y la pantalla de validación de una factura solo ofrece los de
**origen 02 COMPRAS**.

**Estado: validado.** La propuesta original fue aceptada, con una modificación de alcance.

## ⚠️ Decisión de demo, no contable

> Los **22 motivos que corresponden realmente a `07` CAJA CHICA se reclasificaron a `02` COMPRAS**
> por necesidad de la demostración, para que aparezcan en el registro de compras.
>
> **Contablemente son de caja chica.** Esta reclasificación **no es la clasificación real de la
> compañía** y debe revertirse antes de una puesta en producción. Queda escrita aquí para que nadie
> la lea después como criterio contable.

Los motivos afectados están marcados con `02 †` en la tabla.

### Tres casos que darán asientos extraños incluso en la demo

Estos tres no son gastos: sus cuentas son de efectivo o por cobrar, de modo que generarán un asiento
que cuadra pero no representa una compra.

| # | Motivo | Cuenta | Naturaleza real |
|---|---|---|---|
| 5 | Transferencia a Caja chica | `1013,1021,1022` | Movimiento de efectivo |
| 53 | Recarga de tarjetas peruanas | `169901` | Cuenta por cobrar |
| 88 | Devolución Comprobante CChica | `169105` | Cuenta por cobrar |

Si la demo no necesita estos tres en concreto, conviene dejarlos en `07`.

## Reparto final

| Origen | Motivos |
|---|---|
| `02` COMPRAS | **50** (28 propios + 22 reclasificados para la demo) |
| `06` BANCOS | 21 |
| `04` DIARIO | 4 |
| `10` PLANILLAS | 4 |
| `05` CAJA | 3 |
| `03` VENTAS | 2 |
| **BAJA** | **6** |

## Seis motivos dados de baja

No se borran del catálogo: los asientos históricos los referencian y esa referencia debe seguir
resolviendo. `Motivo` lleva un indicador `Activo`; un motivo inactivo **no se ofrece** en el selector
pero sigue existiendo.

| # | Motivo | Razón de la baja |
|---|---|---|
| 1 | Pago a Cuenta de Proveedores | El nombre dice "pago" pero la cuenta `656412` es de suministros y embalajes, la misma del motivo 34 "Cinta de embalajes". Uno de los dos datos está mal. |
| 28 | NO USAR | Marcado así en el propio catálogo. |
| 39 | Otros servicios sin sustentos | Sin sustento no hay comprobante, y sin comprobante no hay factura que registrar. |
| 44 | Servicio cuadrilla sin sustento | Ídem. |
| 76 | Otros gastos sin sustentos | Ídem. |
| 83 | Alquileres | La cuenta `1423` es por cobrar a accionistas, no un gasto de alquiler. |

> **Límite de alcance declarado.** Los tres "sin sustento" existían porque hoy se registra gasto sin
> comprobante. Ese flujo **queda fuera de este sistema**, que parte de una factura recibida por
> correo, y seguirá llevándose por otra vía.

## Tabla completa

`†` = reclasificado de `07` a `02` para la demo.

| # | Motivo | Prefijos | Origen |
|---|---|---|---|
| 1 | Pago a Cuenta de Proveedores | `656412` | **BAJA** |
| 2 | Anticipos a Proveedor | `4221,4321` | 06 |
| 3 | Entrega a rendir Cta. | `141301,141302,1424,169103,169104` | 05 |
| 4 | Transferencia a Caja | `1011,1012` | 05 |
| 5 | Transferencia a Caja chica | `1013,1021,1022` | **02** † |
| 6 | Transferencia entre Bancos | `104` | 06 |
| 7 | Préstamos a Empleados | `141101,141102` | 06 |
| 8 | Tributos Por Pagar | `4011,4017,4018,403,417,167101,1674` | 06 |
| 9 | Deposito en garantia alquiler | `1643` | 06 |
| 10 | Remuneraciones por pagar | `4111,4113,4114,4115,4131,4151,4191` | 10 |
| 11 | Servicio custodia mercad.SS | `639922` | **02** |
| 12 | Fotocopia-Impresión | `639914` | **02** |
| 13 | Movilidad | `631123` | **02** † |
| 14 | Refrigerio al personal | `625111` | **02** |
| 15 | Cumpleaños al personal | `625113` | **02** |
| 16 | Parqueo o cochera | `6393` | **02** † |
| 17 | Tasas de contratos | `644311` | **02** † |
| 18 | Peaje | `639915` | **02** † |
| 19 | Utiles de escritorio menores | `656111` | **02** † |
| 20 | Utiles de Limpieza menores | `656211` | **02** † |
| 21 | Botiquin menores | `656212` | **02** † |
| 22 | Fletes traslado de mercaderia | `631111` | **02** |
| 23 | Combustibles Unidades de trans | `656311,656312` | **02** |
| 24 | Lubricantes Unidades Transpor | `656313` | **02** |
| 25 | Repuestos Unidades Transporte | `656314` | **02** |
| 26 | Devol.prestamo a relacionada | `17` | 06 |
| 27 | Utilidades Socios | `142301,142302,142303` | 06 |
| 28 | NO USAR | `1424` | **BAJA** |
| 29 | Comisiones ventas o gastos | `142304,142305` | 06 |
| 30 | Mantenimiento localmenores | `634311` | **02** † |
| 31 | Prestamos a EmpresasRelacionad | `17` | 06 |
| 32 | Comisión deposito de tercero | `639134` | 06 |
| 33 | Envio de documentos | `631211` | **02** |
| 34 | Cinta de embalajes-sucursales | `656412` | **02** |
| 35 | Bolsas -sucursales | `656411` | **02** |
| 36 | Liquidaciones por Cese | `4114,4115,4151` | 10 |
| 37 | Deposito cta corriente(vuelto) | `104` | 06 |
| 38 | Copia Literal o vigencia pode | `636913` | **02** † |
| 39 | Otros servicios sin sustentos | `639921` | **BAJA** |
| 40 | Legalizaciones | `632211` | **02** † |
| 41 | Recarga de toner | `656111` | **02** |
| 42 | Recarga denextel<100 | `636412` | **02** † |
| 43 | Servicio de cuadrilla | `639917` | **02** |
| 44 | Servicio cuadrilla sin sustent | `639921` | **BAJA** |
| 45 | Servicio custodia Importación | `639922` | **02** |
| 46 | Repuesto soporte tecnico <50 | `656511` | **02** † |
| 47 | Devolución- entrega a rendir | `141303,141304` | 05 |
| 48 | Gastos de representación<100 | `6373` | **02** † |
| 49 | Servicio Reparación equipo<50. | `634314` | **02** † |
| 50 | Anuncio en periodico personal | `637211` | **02** |
| 51 | Muestras de mercaderia | `601111` | **02** |
| 52 | Remesa en transito | `103` | 06 |
| 53 | Recarga de tarjetas peruanas | `169901` | **02** † |
| 54 | Recarga de extintores | `656614` | **02** |
| 55 | Multas fiscales | `6592` | 04 |
| 56 | Reniec | `636912` | **02** † |
| 57 | Uniforme para el personal | `625112` | **02** |
| 58 | Mantenimiento Unidades Transpo | `634212,634312` | **02** |
| 59 | Tasas Judiciales y Policiales | `644311` | **02** † |
| 60 | Arreglo Floral | `659913` | **02** † |
| 61 | Pago de CTS | `41` | 10 |
| 62 | Servicio de Vigilancia ss | `639922` | **02** |
| 63 | Anticipo de Cliente(Devol) | `122` | 03 |
| 64 | Adelanto de alquileres | `1831` | **02** |
| 65 | Arbitrios Municipales | `643211` | **02** |
| 66 | Devolución prestamo Socios | `44` | 06 |
| 67 | Cajas Contables | `656414` | **02** |
| 68 | Devol.nota de credito Cliente | `121201,121202` | 03 |
| 69 | Remuneraciones por pagar vc | `4111` | 10 |
| 70 | Prestamos a terceros | `16` | 06 |
| 71 | Comision pago de ServiciosPubl | `639130` | 06 |
| 72 | Refrigerio del personal SS | `625116` | **02** |
| 73 | Pago de percepción | `401131` | 06 |
| 74 | Reclamaciones a terceros | `4611` | 04 |
| 75 | Compra de equipos diversos | `656613` | **02** |
| 76 | Otros gastos sin sustentos | `659914` | **BAJA** |
| 77 | Periódico | `659914` | **02** † |
| 78 | Comisión planilla trabajadores | `639131` | 06 |
| 79 | Prestamo a Socios | `1422` | 06 |
| 80 | Intereses moratorios | `645` | 06 |
| 81 | Movilidad-Taxi por viaje | `631124` | **02** † |
| 82 | Comisiones de ventas reclamaci | `4611` | 04 |
| 83 | Alquileres | `1423` | **BAJA** |
| 84 | Flete traslado entre almacenes | `631112` | **02** |
| 85 | Depósito de garantia  Aduanas | `164401` | 06 |
| 86 | Cuentas por cobrar a terceros | `1699` | 04 |
| 87 | Adelanto de sueldo | `1412` | 06 |
| 88 | Devolución Comprobante CChica | `169105` | **02** † |
| 89 | Atención Medica | `625117` | **02** |
| 90 | Mantenimiento rep muebles y eq | `634313` | **02** † |
