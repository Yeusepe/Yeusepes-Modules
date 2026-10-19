#include <openssl/curve25519.h>
#include <cstdint>

// This ABI belongs to Melody. BoringSSL is statically linked at the pinned revision.
#define JAM_API extern "C" __declspec(dllexport)
JAM_API SPAKE2_CTX* jam_pake_new(int host, const uint8_t* mine, size_t mine_len,
                                const uint8_t* peer, size_t peer_len) {
  if (host != 0 && host != 1) return nullptr;
  return SPAKE2_CTX_new(host ? spake2_role_bob : spake2_role_alice,
                        mine, mine_len, peer, peer_len);
}
JAM_API void jam_pake_free(SPAKE2_CTX* ctx) { SPAKE2_CTX_free(ctx); }
JAM_API int jam_pake_start(SPAKE2_CTX* ctx, const uint8_t* password,
                           size_t password_len, uint8_t* message) {
  size_t written = 0;
  return ctx && message && password && password_len == 6 &&
    SPAKE2_generate_msg(ctx, message, &written, 32, password, password_len) && written == 32;
}
JAM_API int jam_pake_finish(SPAKE2_CTX* ctx, const uint8_t* peer,
                            size_t peer_len, uint8_t* key) {
  size_t written = 0;
  return ctx && key && peer && peer_len == 32 &&
    SPAKE2_process_msg(ctx, key, &written, 64, peer, peer_len) && written == 64;
}
JAM_API int jam_pake_abi() { return 1; }
