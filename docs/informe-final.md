# Informe final de QA – FlashStore

**Curso:** 048 Aseguramiento de la Calidad del Software · UMG, Campus Cobán
**Equipo:** María Guadalupe Sagui (MaryG0107) · Fernando (kcfernando-dev)
**Fecha:** 6 de noviembre de 2026
**Repositorio:** https://github.com/MaryG0107/ProyectoFinal-QA
**Versión evaluada:** rama `feature/pipeline-completo`, ambiente QA en Google Cloud (35.208.239.0)

---

## 1. Resumen

FlashStore es una tienda en línea de ventas relámpago: pocas unidades, muchos compradores al mismo tiempo. El riesgo principal del negocio es vender más unidades de las que hay. Montamos el ambiente en Google Cloud con Terraform y Ansible y armamos dos pipelines en Jenkins. El segundo corre 14 herramientas de prueba y seguridad y decide si la versión sale (**GO**) o no (**NO-GO**) sin que nadie la apruebe a mano.

Resultado final: el **build #10 del pipeline 2 dio GO**. Antes de llegar ahí, el pipeline dio NO-GO dos veces por razones reales:

| Build | Decisión | Motivo |
|---|---|---|
| #8 | NO-GO | Estrategia de inventario `Unsafe`: sobreventa en la venta relámpago (escenario `v1.0.0-rc1`, intencional) |
| #9 | NO-GO | `dotnet format` con cambios pendientes, 1 posible secreto (gitleaks) y 12 vulnerabilidades CVSS ≥ 7 (Dependency-Check) |
| #10 | **GO** | Todos los criterios en verde |

Los hallazgos del #9 resultaron ser un problema de formato real y dos falsos positivos. Los corregimos o justificamos y documentamos sin bajar ningún umbral (sección 6).

## 2. Arquitectura probada

| Capa | Tecnología |
|---|---|
| Cliente web, desktop y móvil | PWA (React) servida con Nginx sobre Alpine 3.23 |
| API y Worker | .NET 8, imágenes `chiseled-extra` sin shell |
| Datos | PostgreSQL 16, Redis (cache y sesiones), RabbitMQ (outbox, reintentos y DLQ) |
| Infraestructura | Google Cloud (VM e2-medium, us-central1), Terraform con estado en GCS, Ansible |
| CI/CD | Jenkins 2.568.2 en la VM Ubuntu local, SonarQube Community en la misma VM |
| Herramienta propia | CLI `flashstore` (.NET 8): salud, base de datos, venta simulada, colas, decisión GO/NO-GO |

La decisión de cambiar de Azure a Google Cloud está en `docs/decisiones-tecnicas.md` (D-17): Azure for Students no se pudo activar con ninguna de las dos cuentas.

## 3. Pipelines

**Pipeline 1 – `flashstore-infra` (`Jenkinsfile.infra`).** Valida Terraform, muestra el plan, espera la aprobación manual, crea o destruye la infraestructura, despliega con Ansible (incluidas las migraciones de base de datos) y verifica con el CLI. Build de referencia: **#12**, con cupones desplegados.

**Pipeline 2 – `flashstore-seguridad` (`Jenkinsfile`).** Etapas en orden:

1. Preparar: toma la IP del estado remoto de Terraform.
2. Análisis estático en paralelo: ESLint, Semgrep, gitleaks, checkov, `dotnet list package --vulnerable`, npm audit.
3. Lint .NET (`dotnet format`) y pruebas unitarias.
4. SonarQube con espera del Quality Gate.
5. OWASP Dependency-Check.
6. trivy sobre las 3 imágenes.
7. OWASP ZAP sobre la PWA y la API.
8. Pruebas funcionales en la nube: Insomnia (`inso`), Cypress y venta relámpago con 300 compradores.
9. Rendimiento con k6 (carga, estrés y pico).
10. Compuerta de calidad, compuerta de seguridad y decisión GO/NO-GO con `flashstore report go-no-go --strict --security`.

Si la decisión es NO-GO, el comando devuelve código 1, el build queda en rojo y la versión no se promueve.

## 4. Resultados por tipo de prueba (build #10)

### 4.1 Unitarias, API y E2E

| Tipo | Herramienta | Resultado |
|---|---|---|
| Unitarias | xUnit | 85 de 85 aprobadas (incluye reglas de cupones y argumentos SSH del CLI) |
| API | Insomnia / `inso` | 69 casos, 111 aserciones; incluye la regresión de cupones (grupo 07B, 10 casos) |
| E2E web | Cypress 15.21.1 | Aprobado en el pipeline (login, compra, modo sin conexión) |
| E2E móvil | Maestro Web 2.12.1 (Beta) | 3 de 7 flujos; limitación de la herramienta, ver INC-05 |
| Funcional manual | Navegador | Pago aprobado (pedido #66CBB265, "Pagado") y pago rechazado (pedido #7F656E9C, "Fondos insuficientes"), con evidencia en capturas |

### 4.2 Venta relámpago (riesgo principal)

300 compradores intentan comprar al mismo tiempo el producto 1, que tiene 5 unidades. Estrategia del servidor: `Atomic`.

| Indicador | Valor |
|---|---|
| Sesiones iniciadas | 300 / 300 |
| Pedidos creados (201) | **5** |
| Rechazados por stock (409) | 295 |
| Errores 5xx | 0 |
| Stock inicial → final | 5 → 0 |
| Duración total | 2.66 s |
| p95 de `POST /api/orders` | 2 617 ms |

**Sin sobreventa.** Con `Unsafe` (build #8) la misma prueba vendió más de 5 unidades y el pipeline dio NO-GO, como estaba previsto para `v1.0.0-rc1`.

El p95 de 2 617 ms está por encima de la meta de 800 ms que pusimos en el plan para este escenario. La explicación: los 300 pedidos llegan en el mismo instante y todos actualizan **la misma fila** de inventario. El `UPDATE ... WHERE stock >= cantidad` los pone en fila uno detrás de otro dentro de PostgreSQL, en una sola VM e2-medium de 2 vCPU. Es el precio de garantizar que no haya sobreventa. Ese criterio no bloquea el GO (el bloqueante es la sobreventa), pero lo registramos como **riesgo residual RR-08** con una propuesta de mejora en la sección 8.

### 4.3 Rendimiento (k6)

Umbrales declarados en `tests/performance/carga.js` antes de ejecutar:

| Escenario | Carga | Umbral p95 | Umbral de errores |
|---|---|---|---|
| Carga | 50 usuarios constantes, 2 min | < 800 ms | < 1 % |
| Estrés | Sube hasta 200 usuarios | < 1 500 ms | < 5 % |
| Pico | Salta a 300 usuarios | < 2 000 ms | < 5 % |

Resultado global de la corrida (los tres escenarios juntos):

| Métrica | Valor |
|---|---|
| Solicitudes HTTP | 5 703 (46.6 por segundo) |
| Solicitudes fallidas | **0.00 %** |
| Tiempo de respuesta promedio | 105.21 ms |
| Mediana | 103.04 ms |
| p90 | 112.55 ms |
| **p95** | **120.70 ms** |
| p99 | 244.06 ms |
| Máximo | 456.44 ms |

Todos los umbrales se cumplieron. El p95 de 120.7 ms queda muy por debajo incluso del umbral más estricto (800 ms). La lectura del catálogo pasa por Redis, y eso se nota: casi todas las respuestas caen entre 85 y 115 ms, que es básicamente la latencia de red entre Cobán y us-central1.

### 4.4 Calidad

| Herramienta | Resultado |
|---|---|
| ESLint | 0 errores, 0 advertencias en 20 archivos |
| `dotnet format` | Sin cambios pendientes (en el #9 había 5 archivos del CLI con sangría fuera de formato) |
| SonarQube | Quality Gate **OK** |

### 4.5 Seguridad

| Herramienta | Qué revisa | #9 | #10 |
|---|---|---|---|
| gitleaks | Secretos en el historial de Git | 1 (falso positivo FP-02) | 0 |
| `dotnet` | Paquetes NuGet vulnerables | 0 | 0 |
| npm audit | Dependencias de producción de la PWA | 0 | 0 |
| trivy | 3 imágenes Docker | 0 HIGH/CRITICAL | 0 |
| checkov | Terraform | 23 aprobados, 0 fallidos, 2 riesgos aceptados | igual |
| OWASP ZAP | PWA y API en ejecución | 0 altas, 0 medias | igual |
| Semgrep (SAST, en lugar de Fortify) | Código fuente | 0 ERROR, 4 WARNING | igual |
| OWASP Dependency-Check | Dependencias contra la NVD | 12 con CVSS ≥ 7 (falso positivo FP-03) | 0 |

**Evolución a lo largo del proyecto:** trivy empezó con 65 vulnerabilidades HIGH/CRITICAL en las imágenes. Bajaron a 10 al cambiar a las imágenes `chiseled` de Microsoft y a 0 al servir la PWA con el Nginx parcheado de Alpine (DEF-03). checkov marcaba CKV_GCP_32 hasta que corregimos el tipo del valor `block-project-ssh-keys` (DEF-04).

Los 2 riesgos aceptados de checkov son CKV_GCP_40 (la VM tiene IP pública, necesaria para probar desde el equipo; el firewall solo admite nuestras IP) y CKV_GCP_38 (cifrado administrado por Google en lugar de llaves propias en un ambiente efímero). Están justificados en el código con `#checkov:skip` y en D-18.

Las 4 advertencias de Semgrep no bloquean según la política. Quedan para revisión en la siguiente iteración.

### 4.6 Usabilidad y accesibilidad

**Prueba con usuarios (piloto).** Se hizo con 1 participante usando el formulario `docs/usabilidad-formulario.html`:

| Participante | Tareas logradas | SUS |
|---|---|---|
| P1 | 5 de 5 | **75.0** |

El SUS de 75 supera la meta de 68 y la tasa de éxito fue de 100 %. Los tiempos por tarea no se midieron con cronómetro, así que no se reportan. Con un solo participante el resultado sirve como piloto y no se puede generalizar: el plan pedía 5 (ver RR-09).

**Lighthouse:** resultados en `docs/usabilidad-resultados.xlsx`, hoja "Lighthouse" (catálogo, `/login` y `/carrito`, modo móvil).

### 4.7 Mensajería

El pipeline ejecuta `flashstore queue dlq` después de las pruebas funcionales para revisar las colas de mensajes fallidos. El criterio de mensajería forma parte de la decisión estricta del build #10, que dio GO.

## 5. Decisión GO/NO-GO del build #10

| Criterio | Obligatorio | Resultado | Detalle |
|---|---|---|---|
| Salud | Sí | PASA | API healthy; PostgreSQL, Redis y RabbitMQ arriba |
| Inventario | Sí | PASA | Estrategia `Atomic` |
| Venta relámpago | Sí | PASA | 5 pedidos con stock 5; 295 rechazados; sin sobreventa |
| Unitarias / API / E2E | Sí | PASA | Sin fallas |
| Rendimiento | Sí | PASA | Umbrales de k6 cumplidos |
| Mensajería | Sí | PASA | 0 mensajes en DLQ |
| Calidad | Sí | PASA | Lint y Quality Gate en verde |
| Seguridad | Sí | PASA | 0 bloqueantes en las 8 herramientas |

> **GO:** la versión desplegada en 35.208.239.0 cumple los criterios de salida. (`Finished: SUCCESS`)

## 6. Defectos

Detalle completo en `docs/registro-defectos.md`.

| ID | Título | Severidad | Estado |
|---|---|---|---|
| DEF-00 | Sobreventa con estrategia `Unsafe` (intencional, rc1) | Crítica | Corregido con `Atomic` |
| DEF-01 | Secretos impresos en el log de Jenkins | Crítica | Corregido, secretos rotados |
| DEF-02 | Jenkins se queda esperando después de Ansible | Media | Corregido |
| DEF-03 | 65 vulnerabilidades HIGH/CRITICAL en las imágenes | Alta | Corregido (0) |
| DEF-04 | Control CKV_GCP_32 fallido | Media | Corregido |
| DEF-05 | Venta relámpago sin número de compradores | Media | Corregido |
| DEF-06 | Archivos del CLI fuera del formato de `dotnet format` | Baja | Corregido |
| FP-01 | gitleaks: texto de ejemplo en `.env.example` | — | Falso positivo |
| FP-02 | gitleaks: el mismo texto citado en el registro de defectos | — | Falso positivo |
| FP-03 | Dependency-Check confunde StackExchange.Redis con el servidor Redis | — | Falso positivo |

Por severidad: 2 críticos, 1 alto, 3 medios y 1 bajo. Los 7 están corregidos. De las 5 incidencias de ambiente (INC-01 a INC-05), solo INC-05 queda abierta como limitación de la herramienta.

**Cómo tratamos los falsos positivos.** En ningún caso desactivamos la herramienta ni subimos el umbral. Cada supresión apunta a una sola coincidencia y lleva su justificación. En gitleaks, la huella exacta va a `.gitleaksignore` (commit, archivo, regla y línea). En Dependency-Check usamos `dependency-check-suppressions.xml`, que suprime solo el CPE `cpe:/a:redis:redis` en el paquete `StackExchange.Redis`. Si mañana aparece un secreto nuevo o un CVE real del cliente de Redis, el pipeline lo detecta. La evidencia de que FP-03 es falso positivo está en las otras dos herramientas: `dotnet list package --vulnerable` (base GitHub Advisory) y trivy reportan 0 para ese paquete.

## 7. Casos de prueba

`docs/casos-prueba.xlsx` tiene 36 casos (CP-01 a CP-36) trazados a los riesgos de `docs/matriz-riesgos.xlsx`. Los casos automatizados quedan cubiertos por las corridas del build #10. Los casos manuales se ejecutaron en el navegador sobre el ambiente QA.

## 8. Riesgos residuales

| ID | Riesgo | Nivel | Tratamiento propuesto |
|---|---|---|---|
| RR-01 | IP pública de la VM de QA (CKV_GCP_40) | Bajo | Aceptado; firewall limitado a las IP del equipo; en producción, balanceador con IP fija y VM sin IP pública |
| RR-02 | Cifrado administrado por Google (CKV_GCP_38) | Bajo | Aceptado para QA; CMEK en producción |
| RR-03 | 4 advertencias de Semgrep sin revisar | Bajo | Revisarlas en la siguiente iteración |
| RR-04 | Supresiones de falsos positivos que podrían ocultar algo futuro | Bajo | Supresiones acotadas a una coincidencia; revisarlas en cada release |
| RR-05 | Dependency-Check sin llave NVD (base descargada el 6/11, `--noupdate`) | Medio | Pedir la llave NVD y actualizar la base antes de cada release |
| RR-06 | Un solo nodo (VM única): sin alta disponibilidad | Medio | Fuera del alcance del curso; en producción, servicios administrados (Cloud SQL, Memorystore) |
| RR-07 | Jenkins y SonarQube en la VM local de una laptop | Medio | Mover a un servidor o a un servicio de CI en la nube |
| RR-08 | p95 de 2.6 s en `POST /api/orders` durante la ráfaga de 300 compradores | Medio | Cola de reservas en Redis (`DECR` atómico) delante de PostgreSQL, o una VM más grande; la sobreventa ya está controlada |
| RR-09 | Prueba de usabilidad con 1 participante | Medio | Completar con 5 participantes y cronometrar las tareas |
| RR-10 | Flujos móviles con Maestro Web en Beta (INC-05) | Bajo | Cubiertos por Cypress; repetir con Maestro en Android o cuando salga de Beta |

## 9. Lecciones aprendidas

1. **El NO-GO también es un buen resultado.** El build #9 encontró un problema real de formato y nos obligó a revisar dos alertas a fondo. Un pipeline que nunca falla no prueba nada.
2. **Revisar el log además del resultado.** El DEF-01 (secretos en el log de Jenkins) no lo detectó ninguna herramienta: apareció al leer el log. Lo corregimos con `set +x` y rotamos los secretos.
3. **Documentar un falso positivo puede generar otro.** Al citar el texto de `.env.example` en el registro de defectos, gitleaks lo volvió a encontrar (FP-02).
4. **Una herramienta en Beta no sirve como evidencia de salida.** Maestro Web no cambiaba el foco entre casillas. Que Cypress ya cubriera esos flujos evitó que eso bloqueara la entrega.
5. **El ambiente también falla.** El reloj de la VM, Smart App Control de Windows y la versión de Node nos costaron horas (INC-01 a INC-03). Dejarlos documentados ahorra tiempo la próxima vez.

## 10. Evidencias

| Evidencia | Dónde |
|---|---|
| Builds conservados del pipeline 2 | Jenkins `flashstore-seguridad`: #5, #7, #8 (NO-GO Unsafe), #9 (NO-GO hallazgos), #10 (GO) |
| Builds conservados del pipeline 1 | Jenkins `flashstore-infra`: #7, #11, #12 |
| Reportes HTML | Jenkins, en cada build: ZAP, Dependency-Check, Cypress, k6 |
| Reportes JSON | Artefactos del build: `reports/security/`, `reports/quality/`, `reports/cli/` |
| Capturas | Carpeta de evidencias entregada junto con este informe |
| Código y documentación | Repositorio, ramas `develop` y `main`, releases `v1.0.0-rc1` y `v1.0.0-rc2` |
