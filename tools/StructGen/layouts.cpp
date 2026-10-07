// Inclui os headers de structs do cliente (rebang) para o clang despejar o layout MSVC 32 bits.
// Usado só por tools/StructGen/dump.sh; não faz parte do servidor.
// As declarações abaixo substituem o que os headers usam da biblioteca do Windows (só para compilar).
extern "C" void* memset(void*, int, unsigned int);
extern "C" int rand();
namespace std { template <class T> struct list { void* a; void* b; unsigned int n; void clear() {} }; }
struct WSendPacket { void Encode1(unsigned char); void Encode4(unsigned long); };
typedef struct _SYSTEMTIME
{
	unsigned short wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
} SYSTEMTIME;
#include "globalgamedefine.h"
#include "globalnetworkdefine.h"
#include "classdefine.h"
