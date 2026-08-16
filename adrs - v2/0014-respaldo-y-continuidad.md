# ADR 0014: Respaldo y continuidad

## Estado

Aceptado. Decisión nueva. Ni el diseño anterior ni ninguna de sus diez ADRs mencionaban respaldo,
frecuencia de copia, prueba de restauración, retención ni ubicación fuera del host.

## Contexto

El PRD elimina deliberadamente el sistema contable externo: *"el asiento generado se guarda
únicamente en la base de datos asignada al software; no hay integración ni migración hacia ningún
sistema de gestión contable externo"*, y añade conservación indefinida.

Esa decisión convierte a **una instancia de SQL Server en el libro de compras de la empresa, sin
copia en ningún otro sistema**. Antes existía un sistema contable en paralelo que sostenía el
registro; el diseño acaba de retirarlo.

Google Drive **no cubre este hueco**: contiene los adjuntos de las facturas validadas, pero no los
asientos, ni las correcciones, ni la auditoría, ni el estado de nada.

Y ADR 0013 añade un segundo almacén: el volumen de documentos, que vive fuera de la base.

## Decisión

### Respaldo escalonado, con orden deliberado

| Frecuencia | Acción |
|---|---|
| Diario, paso 1 | Copia del **volumen de documentos** |
| Diario, paso 2 | `FULL BACKUP` de la base de datos |
| Cada 15 minutos | `LOG BACKUP` |

### Por qué el orden importa

El volumen se copia **antes** que la base. Así toda fila que referencia un archivo tiene ese archivo
presente en la copia.

En el orden inverso, la base respaldada contendría referencias a documentos que la copia del volumen
todavía no capturó: **asientos contables sin su evidencia**. Es la mitigación concreta del costo que
ADR 0013 asume al elegir disco compartido.

### Objetivos

- **RPO = 15 minutos.** Pérdida máxima aceptable de trabajo.
- **Destino fuera del host.** Un respaldo en el mismo disco que protege no es un respaldo.
- **RTO:** por definir.
- **Retención:** por definir, contra el requisito de conservación indefinida del PRD.

### Alcance

El respaldo cubre la base de datos, el volumen de documentos y **el almacén de secretos** (ADR
0015): si este último se pierde, se pierde el acceso a Gmail, Drive, Sheets y Telegram.

### Prueba de restauración

Se ejecuta una **prueba de restauración periódica documentada**.

> Un respaldo que nunca se restauró no es un respaldo, es una suposición. Y esto es la contabilidad
> de compras completa de la empresa.

## Alternativas consideradas

- **Snapshot de la máquina completa.** Captura base y volumen en el mismo instante, lo que elimina
  de raíz el problema de coordinación entre los dos almacenes. Se descartó porque exige
  infraestructura de virtualización con soporte consistente para SQL Server, que no está decidida, y
  porque su RPO típico de 24 horas significa perder hasta un día de trabajo.
- **Respaldo directo a almacenamiento en la nube.** La copia sale del host por diseño, sin segundo
  servidor. Se descartó como mecanismo primario porque introduce credenciales de almacenamiento,
  costo recurrente y la necesidad de cifrar respaldos que son la contabilidad completa de la empresa
  saliendo de la red interna. Sigue siendo válido como destino secundario.
- **Confiar en Drive como copia.** Se descartó porque Drive solo tiene los adjuntos de las facturas
  ya validadas: ni asientos, ni auditoría, ni estado, ni los documentos previos a la validación.

## Consecuencias

- El mayor riesgo de negocio del proyecto tiene una respuesta explícita y verificable.
- Los dos almacenes se respaldan de forma consistente entre sí.
- **Costo:** el respaldo de log cada 15 minutos exige el modelo de recuperación completo y la
  gestión de su crecimiento.
- **Costo:** la prueba de restauración consume tiempo del operador de forma periódica. Es el costo
  que hace que el respaldo sea real.
- **Costo:** la retención frente a conservación indefinida crece de forma no acotada. Se dimensiona
  junto con el umbral de espacio libre de ADR 0015.
- **No cubre la disponibilidad.** Esto es recuperación ante pérdida, no continuidad de servicio. El
  worker no tiene vigilancia automática y su reinicio es manual (ADR 0001).
