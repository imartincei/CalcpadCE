use std::path::PathBuf;

fn main() {
    // `npm run dev`/`build` stage the real docs; plain cargo runs only need the resource glob to match.
    let docs = PathBuf::from(std::env::var("CARGO_MANIFEST_DIR").unwrap()).join("agent-docs");
    let empty = std::fs::read_dir(&docs).map_or(true, |mut entries| entries.next().is_none());
    if empty {
        std::fs::create_dir_all(&docs).expect("could not create agent-docs");
        std::fs::write(
            docs.join("README.md"),
            "Staged by tools/agent-docs.mjs on `npm run dev` and `npm run build`.\n",
        )
        .expect("could not write the agent-docs placeholder");
    }
    tauri_build::build()
}
