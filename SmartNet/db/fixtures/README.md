# Fixtures de entorno

**Esto no es parte del esquema versionado y en producción no se ejecuta.**

Los cinco catálogos maestros los mantiene el **sistema contable de la compañía**, y este proyecto
solo tiene `SELECT` sobre ellos (ADR 0003, clase *externa*). El esquema versionado de
`SmartNet/db/schema/` **nunca crea ni escribe un objeto en `dbo`**, y esa invariante es la propiedad
más fuerte que reivindica ADR 0003.

Estos scripts existen por una razón concreta: la base asignada está vacía. Sin los catálogos no se
puede crear una referencia, ni otorgar un `GRANT SELECT`, ni sembrar `MotivoAtributo`. Viven aquí,
fuera de `schema/`, precisamente para que la invariante se conserve intacta.

## Contenido

| Archivo | Qué hace |
|---|---|
| `exportar-catalogos.ps1` | Lee los `.xlsx` de la raíz y escribe los CSV de `data/`. No toca la base. |
| `010_dbo_catalogos_ddl.sql` | Crea las cinco tablas en `dbo`. |
| `020_dbo_catalogos_datos.sql` | Carga los CSV con `BULK INSERT` y verifica el resultado. |
| `data/*.csv` | Los datos, delimitados por barra vertical. |

## Cómo se usa

```powershell
# 1. Regenerar los CSV desde los .xlsx (solo si cambiaron los datos maestros)
.\exportar-catalogos.ps1

# 2. Crear las tablas y cargarlas
sqlcmd -S localhost -E -d BDSmartNet -i .\010_dbo_catalogos_ddl.sql
sqlcmd -S localhost -E -d BDSmartNet -i .\020_dbo_catalogos_datos.sql
```

`020` es **idempotente**: vacía cada tabla antes de cargarla, así que se puede repetir.

> `BULK INSERT` lee las rutas **desde el servidor**, no desde el cliente. Si SQL Server no corre en
> esta máquina, hay que copiar `data\` a una ruta que el servidor alcance y ajustar `@ruta` en `020`.

## Qué se normaliza al exportar, y por qué

El sistema de origen exporta columnas `CHAR` rellenadas con espacios, así que **todo valor se
recorta**. Además:

**Se retira el prefijo `DNI` del número de documento.** 118 de las 6600 filas traían valores como
`DNI77343951`. El tipo de documento ya vive en `coddocide`, de modo que el prefijo era información
duplicada dentro del número. Retirado, quedan 8 dígitos — exactamente un DNI peruano.

**Se decodifican entidades XML escapadas**, y una corrupción puntual: la comilla doble del proveedor
`P00001` venía codificada como `&AMPQUOT`, que no es una entidad válida. Afecta a **una sola fila**.
Los otros 230 `&` del catálogo son legítimos —nombres del tipo `A & B S.A.`— y no se tocan.

## Cifras medidas, no supuestas

Todo lo que sigue se contó sobre los archivos reales:

| Catálogo | Filas | Dato notable |
|---|---|---|
| `DocumentoIdentidad` | 6 | `00` otros · `01` DNI · `04` carné · `06` RUC · `07` pasaporte |
| `Origen` | 13 | Códigos de 2 caracteres |
| `Motivo` | 90 | `cuenta` guarda **prefijos separados por coma**, no cuentas completas |
| `CuentaContable` | 1650 | **907** miden 6 dígitos: son las imputables |
| `Proveedor` | 6600 | El genérico es **`P00000`**, seis caracteres |

Tres de estas cifras corrigieron o confirmaron algo que los documentos afirmaban:

- **`P00000`, no `P0000`.** Los documentos decían cinco caracteres. Son seis, en las 6600 filas. No
  era una errata: la invariante *"validar con proveedor genérico se rechaza"* comparaba contra un
  valor que ninguna fila tiene, de modo que nunca se habría disparado.
- **907 cuentas imputables.** El catálogo deja `nivel` vacío exactamente en esas 907, y lo llena en
  las 743 restantes. La cifra que los documentos citaban queda confirmada por el propio dato.
- **El número de documento no siempre es RUC.** 6476 filas traen RUC de 11 dígitos, 118 un DNI de 8
  y 6 un carné de extranjería de 9 o 10. Los ceros a la izquierda son significativos, así que la
  columna es texto y **nunca numérica**.

## Una consecuencia que estaba abierta, y ya no

`Factura.RucProveedor` se diseñó como 11 dígitos, que es lo correcto para el emisor de un
comprobante. Pero **124 proveedores del catálogo no tienen RUC de 11 dígitos**: tienen DNI o carné.
Con el diseño original, una factura de cualquiera de ellos habría sido rechazada por la restricción.

**Resuelto con criterio contable: esos emisores son legítimos.** La columna admite ahora de 8 a 11
dígitos, en `fact.Factura` y en `fact.DatosExtraidos`.

Y pasó de `CHAR(11)` a `VARCHAR(11)`, que es la mitad menos obvia de la corrección: un tipo de
longitud fija habría rellenado con espacios un DNI de 8 dígitos, de modo que nunca habría sido igual
al valor de `dbo.Proveedor.rucpro` —que es `VARCHAR`— y habría entrado relleno en
`IX_Factura_Identidad`, dejando de detectar duplicados sin avisar. Es la misma clase de defecto que
el retorno de carro invisible que arrastró una vez la carga de catálogos.
