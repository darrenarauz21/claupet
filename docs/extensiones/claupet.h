/*
 * claupet.h — la ABI de C de las extensiones nativas de Claupet (versión 1).
 *
 * Una extensión «native» es una biblioteca compartida (.dll en Windows, .so en Linux, .dylib en macOS)
 * escrita en cualquier lenguaje que exporte funciones de C: C, C++, Rust, Go (cgo -buildmode=c-shared),
 * Zig, .NET con NativeAOT ([UnmanagedCallersOnly]), etc.
 *
 * Claupet NO carga la biblioteca en su proceso: la carga en un proceso anfitrión aparte, de la misma
 * arquitectura que Claupet (x64 o arm64). Si la biblioteca se cuelga o revienta, solo cae ese proceso.
 *
 * Todo el texto es UTF-8 terminado en cero. Los mensajes son los mismos del protocolo JSON-RPC
 * (docs/extensiones/README.md): «method» es el nombre del método y «params_json»/«result_json» son
 * los objetos JSON de sus parámetros y su resultado.
 *
 * Licencia: MIT, como Claupet.
 */
#ifndef CLAUPET_H
#define CLAUPET_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32)
#  define CLAUPET_EXPORT __declspec(dllexport)
#else
#  define CLAUPET_EXPORT __attribute__((visibility("default")))
#endif

#define CLAUPET_API_VERSION 1

/* Códigos de error (los de JSON-RPC). 0 es «bien». */
#define CLAUPET_OK                  0
#define CLAUPET_INVALID_PARAMS      (-32602)
#define CLAUPET_METHOD_NOT_FOUND    (-32601)
#define CLAUPET_INTERNAL_ERROR      (-32603)
#define CLAUPET_PERMISSION_DENIED   (-32001)

/*
 * Lo que Claupet le da a la extensión en claupet_init. La tabla vive mientras corre la extensión.
 *
 * call:        le pide algo a Claupet (la API del anfitrión: «pet/say», «notice/show», «voice/speak»…).
 *              Bloquea hasta la respuesta: llámela desde sus propios hilos o desde claupet_call, nunca
 *              desde claupet_init si necesita la respuesta enseguida. *result_json lo libera free_string.
 *              Devuelve 0, o un código de error y en *result_json {"code": n, "message": "…"}.
 * notify:      una notificación, sin respuesta. Con method «$/progress» y params
 *              {"id": <el id de claupet_call>, "value": {…}} manda un avance de esa llamada (los trozos de
 *              texto de una respuesta, o de audio).
 * free_string: libera lo que devolvió call.
 */
typedef struct claupet_host {
    uint32_t size;  /* sizeof(claupet_host) de Claupet: los campos nuevos irán al final */
    void* context;
    int32_t (*call)(void* context, const char* method, const char* params_json, char** result_json);
    void (*notify)(void* context, const char* method, const char* params_json);
    void (*free_string)(char* s);
} claupet_host;

/* Lo que la extensión exporta. */

/* Devuelve CLAUPET_API_VERSION. */
CLAUPET_EXPORT int32_t claupet_api_version(void);

/* Se llama una vez, antes que todo. Guarde el puntero: sirve hasta claupet_shutdown. 0: lista. */
CLAUPET_EXPORT int32_t claupet_init(const claupet_host* host);

/*
 * Atiende un método («initialize», «skill/command», «speechToText/transcribe»…). id identifica la
 * llamada (para sus avances y para claupet_cancel). Devuelve 0 y, en *result_json, el resultado (o NULL
 * para «null»); si no, un código de error y, opcionalmente, {"code": n, "message": "…"}. *result_json lo
 * reserva la extensión y lo libera Claupet con claupet_free.
 *
 * Por defecto se llama desde varios hilos a la vez; con "threading": "single" en el manifiesto, de a una.
 */
CLAUPET_EXPORT int32_t claupet_call(int64_t id, const char* method, const char* params_json, char** result_json);

/* Libera lo que devolvió claupet_call. */
CLAUPET_EXPORT void claupet_free(char* p);

/* Que deje de hacer la llamada id (opcional: si no la exporta, Claupet solo deja de esperar). */
CLAUPET_EXPORT void claupet_cancel(int64_t id);

/* Se llama una vez, al detenerla. Después no se vuelve a llamar nada. */
CLAUPET_EXPORT void claupet_shutdown(void);

#ifdef __cplusplus
}
#endif

#endif /* CLAUPET_H */
