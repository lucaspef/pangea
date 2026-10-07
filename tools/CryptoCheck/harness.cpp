// Gera vetores de teste da cifra usando o código do PRÓPRIO cliente (jrencrypt.cpp + tabelas de packet.cpp).
// Lê linhas da entrada padrão:
//   c2s k seed hexbody  -> pacote como WSendPacket::MakePacketComplete monta (cliente -> servidor)
//   s2c k seed hexbody  -> pacote servidor -> cliente sem compressão, conferido pela decodificação do cliente
//                          (WReceivedPacket::IsValid + CCompressBuffer, flag 1 = sem compressão)
// e imprime o pacote em hex (ou "FAIL").
#include <cstdio>
#include <cstring>
#include <vector>
#include <string>
#include <iostream>
#include "keytables.h"
void SimpleStreamEncrypt_Alpha(const char* src, char* tar, unsigned int len, unsigned int key);
void SimpleStreamDecrypt_Alpha(const char* src, char* tar, unsigned int len, unsigned int key);

static std::vector<unsigned char> unhex(const std::string& s)
{
	std::vector<unsigned char> v;
	for (size_t i = 0; i + 1 < s.size(); i += 2) v.push_back((unsigned char)std::stoi(s.substr(i, 2), nullptr, 16));
	return v;
}

static void print(const std::vector<unsigned char>& p)
{
	for (unsigned char b : p) printf("%02x", b);
	printf("\n");
}

int main()
{
	std::string mode, hex;
	int k, seed;
	while (std::cin >> mode >> k >> seed >> hex)
	{
		std::vector<unsigned char> body = unhex(hex == "-" ? "" : hex);
		unsigned char key = PublicKeyTable[k][seed], check = PrivateKeyTable[k][seed];
		if (mode == "c2s")
		{
			std::vector<unsigned char> p(5);
			p.insert(p.end(), body.begin(), body.end());
			unsigned short len = (unsigned short)(p.size() - 4);
			p[0] = seed; p[1] = len & 0xFF; p[2] = len >> 8; p[3] = 0; p[4] = check;
			SimpleStreamEncrypt_Alpha((const char*)&p[4], (char*)&p[4], len, key);
			print(p);
			continue;
		}
		// s2c: plain = [check][1][3 dígitos base 255, mais significativo primeiro][body]
		std::vector<unsigned char> plain = {check, 1, 0, 0, 0};
		unsigned int size = (unsigned int)body.size();
		for (int i = 0; i < 3; i++) { plain[4 - i] = size % 255; size /= 255; }
		plain.insert(plain.end(), body.begin(), body.end());
		std::vector<unsigned char> w = {(unsigned char)seed, (unsigned char)(plain.size() & 0xFF), (unsigned char)(plain.size() >> 8)};
		std::vector<unsigned char> enc(plain.size());
		SimpleStreamEncrypt_Alpha((const char*)plain.data(), (char*)enc.data(), (unsigned int)plain.size(), key);
		w.insert(w.end(), enc.begin(), enc.end());
		// confere com a decodificação do cliente
		std::vector<unsigned char> dec(enc.size());
		SimpleStreamDecrypt_Alpha((const char*)enc.data(), (char*)dec.data(), (unsigned int)enc.size(), key);
		int written = 0, mul = 1;
		for (int i = 0; i < 3; i++) { written += dec[4 - i] * mul; mul *= 255; }
		bool ok = dec[0] == check && dec[1] == 1 && written == (int)body.size() && !memcmp(dec.data() + 5, body.data(), body.size());
		if (ok) print(w); else printf("FAIL\n");
	}
	return 0;
}
