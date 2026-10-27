/*
 * Una extensión nativa de ejemplo: la habilidad «/eco» repite lo que se le dice y la mascota lo cuenta.
 * También la usan las pruebas de Claupet (Claupet.Tests) para probar el anfitrión de DLL.
 *
 * Compilar (con Zig, que trae un compilador de C para todas las plataformas):
 *   zig cc -shared -O2 -target x86_64-windows-gnu -o eco.dll eco.c
 *   zig cc -shared -O2 -target aarch64-macos -o libeco.dylib eco.c
 *   zig cc -shared -O2 -fPIC -target x86_64-linux-gnu -o libeco.so eco.c
 */
#include "../../claupet.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static const claupet_host* host;

static char* copy(const char* text)
{
    size_t n = strlen(text) + 1;
    char* p = (char*)malloc(n);
    if (p) memcpy(p, text, n);
    return p;
}

/* El valor de texto de "clave" en un JSON plano (suficiente para el ejemplo; use una biblioteca de JSON de verdad). */
static void field(const char* json, const char* key, char* out, size_t size)
{
    char pattern[64];
    snprintf(pattern, sizeof pattern, "\"%s\":\"", key);
    const char* at = strstr(json, pattern);
    out[0] = 0;
    if (!at) return;
    at += strlen(pattern);
    size_t i = 0;
    while (*at && *at != '"' && i + 1 < size) {
        if (*at == '\\' && at[1]) at++;
        out[i++] = *at++;
    }
    out[i] = 0;
}

CLAUPET_EXPORT int32_t claupet_api_version(void) { return CLAUPET_API_VERSION; }

CLAUPET_EXPORT int32_t claupet_init(const claupet_host* h)
{
    host = h;
    return CLAUPET_OK;
}

CLAUPET_EXPORT int32_t claupet_call(int64_t id, const char* method, const char* params, char** result)
{
    char text[512], buffer[1024];
    *result = NULL;

    if (strcmp(method, "initialize") == 0) {
        *result = copy("{\"name\":\"eco\"}");
        return CLAUPET_OK;
    }
    if (strcmp(method, "skill/command") == 0) {
        field(params, "text", text, sizeof text);
        /* Le pide a Claupet que la mascota lo diga (necesita el permiso «pet»). */
        snprintf(buffer, sizeof buffer, "{\"text\":\"%s\"}", text);
        char* answer = NULL;
        int32_t status = host->call(host->context, "pet/say", buffer, &answer);
        if (answer) host->free_string(answer);
        snprintf(buffer, sizeof buffer, "{\"reply\":\"eco: %s\",\"petSaid\":%s}", text, status == 0 ? "true" : "false");
        *result = copy(buffer);
        return CLAUPET_OK;
    }
    if (strcmp(method, "assistant/respond") == 0) {
        /* Dos avances y el final, como un modelo que escribe por partes. */
        snprintf(buffer, sizeof buffer, "{\"id\":%lld,\"value\":{\"text\":\"Hola, \"}}", (long long)id);
        host->notify(host->context, "$/progress", buffer);
        snprintf(buffer, sizeof buffer, "{\"id\":%lld,\"value\":{\"text\":\"soy una DLL.\"}}", (long long)id);
        host->notify(host->context, "$/progress", buffer);
        *result = copy("{\"text\":\"Hola, soy una DLL.\",\"model\":\"eco-1\"}");
        return CLAUPET_OK;
    }
    if (strcmp(method, "test/crash") == 0) {
        abort(); /* revienta: solo debe caer el anfitrión, no Claupet */
    }
    *result = copy("{\"code\":-32601,\"message\":\"metodo desconocido\"}");
    return CLAUPET_METHOD_NOT_FOUND;
}

CLAUPET_EXPORT void claupet_free(char* p) { free(p); }

CLAUPET_EXPORT void claupet_cancel(int64_t id) { (void)id; }

CLAUPET_EXPORT void claupet_shutdown(void) { host = NULL; }
