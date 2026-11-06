# Registro de defectos – FlashStore

Cada defecto también está registrado como **GitHub Issue** con la etiqueta `bug`, enlazado al Pull Request que lo corrigió. Las severidades siguen la tabla de la sección 17 del plan de pruebas.

## Resumen

| ID | Título | Severidad | Prioridad | Detectado por | Estado |
|---|---|---|---|---|---|
| DEF-00 | Sobreventa con estrategia `Unsafe` (defecto intencional, rc1) | Crítica | P1 | Venta relámpago + GO/NO-GO | Corregido (`Atomic`), documentado como escenario NO-GO |
| DEF-01 | Secretos impresos en el log de Jenkins | Crítica | P1 | Revisión manual del log | Corregido, secretos rotados |
| DEF-02 | Jenkins se queda esperando después de Ansible | Media | P2 | Ejecución del pipeline 1 | Corregido |
| DEF-03 | 65 vulnerabilidades HIGH/CRITICAL en las imágenes Docker | Alta | P1 | trivy | Corregido (0) |
| DEF-04 | Control CKV_GCP_32 fallido (llaves SSH del proyecto) | Media | P2 | checkov | Corregido |
| DEF-05 | Venta relámpago sin número de compradores | Media | P2 | Pipeline 2 (criterio "Sin ejecutar") | Corregido |
| FP-01 | gitleaks marca el texto de ejemplo de `.env.example` | — | — | gitleaks | Falso positivo revisado |

Incidencias de ambiente (no son defectos del producto, pero afectaron la ejecución): INC-01 a INC-04, al final.

---

## DEF-00 · Sobreventa con estrategia `Unsafe`

| Campo | Detalle |
|---|---|
| Severidad / prioridad | Crítica / P1 |
| Pasos | 1) Pipeline 1 con `INVENTORY_STRATEGY=Unsafe`. 2) Pipeline 2: `db reset-flash-sale`, luego `sale simulate --buyers 300` |
| Esperado | 5 pedidos creados, 295 rechazados, stock final 0 |
| Obtenido | **252 pedidos con stock 5** (sobreventa de 247 unidades) |
| Causa | La estrategia lee el stock y lo descuenta en dos sentencias separadas; varias transacciones leen "hay 5" antes de que otra descuente (condición de carrera) |
| Corrección | Estrategia `Atomic`: `UPDATE products SET stock = stock - @q WHERE id = @id AND stock >= @q` (D-05) |
| Verificación | Pipeline 2 #7: 5 pedidos, 295 rechazados, **GO** |
| Evidencia | Jenkins `flashstore-seguridad` #8 (NO-GO) y #7 (GO), conservados; capturas fase5-16 y fase5-13 |
| Nota | Defecto **intencional** (D-11) para el release `v1.0.0-rc1` y la demostración del NO-GO |

## DEF-01 · Secretos impresos en el log de Jenkins

| Campo | Detalle |
|---|---|
| Severidad / prioridad | Crítica / P1 |
| Pasos | Ejecutar `flashstore-infra` #6 → abrir el log de la etapa "Ansible: desplegar" |
| Esperado | Las contraseñas no aparecen; Jenkins enmascara las credenciales |
| Obtenido | `FLASHSTORE_POSTGRES_PASSWORD=...`, la clave de RabbitMQ y la llave JWT en texto plano |
| Causa | Jenkins ejecuta `sh` con traza (`-x`); al cargar el archivo de secretos se imprime cada asignación. Jenkins solo enmascara la ruta del archivo, no su contenido |
| Corrección | `set +x` antes de cargar los secretos y `set -x` después (commit `43926f3`); **rotación** de las tres claves y actualización de la credencial `flashstore-secrets` |
| Verificación | Builds posteriores sin secretos en el log; las claves expuestas ya no son válidas |
| Evidencia | Captura fase5-defecto-01 (con las claves tapadas), fase5-18-secretos-rotados |

## DEF-02 · Jenkins se queda esperando después de Ansible

| Campo | Detalle |
|---|---|
| Severidad / prioridad | Media / P2 |
| Pasos | Pipeline 1 → la etapa "Ansible: desplegar" termina con `PLAY RECAP failed=0` |
| Esperado | Pasa a "Verificar con el CLI" |
| Obtenido | La etapa sigue "corriendo" más de 20 minutos sin salida; el aborto normal no la detiene |
| Causa | `ControlPersist=60s` deja un proceso `ssh` maestro en segundo plano con la salida abierta |
| Corrección | `ANSIBLE_SSH_ARGS` con `ControlMaster=no` en el pipeline (commit `d3df1b9`) |
| Verificación | Build #7: Ansible 2 min 11 s y pasa a la verificación con el CLI |

## DEF-03 · Vulnerabilidades HIGH/CRITICAL en las imágenes

| Campo | Detalle |
|---|---|
| Severidad / prioridad | Alta / P1 |
| Pasos | Pipeline 2 → etapa trivy |
| Esperado | 0 vulnerabilidades HIGH/CRITICAL con parche disponible |
| Obtenido | Build #5: **65** (api y worker: `apt`, `libapt-pkg` en Debian 12; web: libexpat, openssl, curl, libpng, brotli en Alpine 3.21.3). Build #6: **10** (paquete `nginx 1.28.2`) |
| Causa | Imágenes base desactualizadas y con gestor de paquetes innecesario |
| Corrección | api y worker con imágenes **chiseled** `8.0-noble-chiseled-extra` (sin shell ni apt, commit `a3e4d73`); PWA con `alpine:3.23` + nginx de Alpine 1.28.3 parcheado y `apk upgrade` (commit `219fb93`) |
| Verificación | Build #7: **0** en las tres imágenes; PWA y API funcionando en la nube |

## DEF-04 · Control CKV_GCP_32 fallido

| Campo | Detalle |
|---|---|
| Severidad / prioridad | Media / P2 |
| Esperado | `block-project-ssh-keys` activo en la VM |
| Obtenido | checkov 3.2.255 no reconoce el valor `"TRUE"` como activado |
| Corrección | Valor booleano `true` en `main.tf` (commit `a3e4d73`) |
| Verificación | checkov: 23 aprobados, 0 fallidos, 2 riesgos aceptados |

## DEF-05 · Venta relámpago sin número de compradores

| Campo | Detalle |
|---|---|
| Severidad / prioridad | Media / P2 |
| Obtenido | `sale simulate --buyers` sin valor → "Sin ejecutar" en el GO/NO-GO |
| Causa | El job se creó copiando otro y su primer build no tenía el parámetro `COMPRADORES` |
| Corrección | Valor por defecto `${COMPRADORES:-300}` |
| Verificación | Build #6: 5 pedidos, 295 rechazados, sin sobreventa |

## FP-01 · Falso positivo de gitleaks

`generic-api-key` en `.env.example`, línea 17: `JWT_KEY=cambie-esta-llave-por-una-aleatoria-de-al-menos-32-caracteres`. Es el texto de ejemplo, no una llave. Se registró en `.gitleaksignore` **solo esa coincidencia** (commit, archivo, regla y línea), así que cualquier secreto nuevo se sigue detectando.

---

## Incidencias de ambiente

| ID | Incidencia | Causa | Solución |
|---|---|---|---|
| INC-01 | Google rechaza el token (`Invalid JWT ... iat and exp`) | Reloj de la VM atrasado 4 h después de suspender la laptop | Sincronización NTP con chrony (`makestep 1 -1`) y `scripts/check-clock.sh` al inicio de cada pipeline |
| INC-02 | Windows bloquea `flashstore.dll` en las pruebas (0x800711C7) | Smart App Control con la DLL nueva sin firma | Pruebas en Linux (VM y Jenkins) |
| INC-03 | `inso` no compila con Node 22 | `node-libcurl` incompatible con V8 de Node 22 | Ejecutable oficial independiente de `inso` 13.3.1 |
| INC-04 | Jenkins no encuentra la rama | Branch Specifier en `*/master` | `*/develop` y ramas de trabajo |
