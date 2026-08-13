// wrangler's `CompiledWasm` rule turns a.wasm import into an already-compiled module
//. Copied next to the emitted entrypoint module so the checker can see the
// import it makes; carries no top-level import or export, which is what keeps it ambient.
declare module "*.wasm" {
  const wasm: WebAssembly.Module;
  export default wasm;
}
