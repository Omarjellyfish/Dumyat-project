# Antigravity Rules

## Rule: Persistent File Indexing & State-Aware Caching (Token Optimization)

### 1. Objective
To prevent redundant directory scanning and repetitive code analysis, minimize token consumption, and optimize search speed by maintaining a local, persistent, state-aware hash map of project files for the Anti Gravity project.

### 2. Core Protocol
Before initiating any directory scan, file search, or deep file analysis, the agent MUST check for the existence of `workspace_index.json` in the root directory.

* **If `workspace_index.json` does NOT exist:** The agent must perform a one-time recursive scan of the project directory (strictly adhering to Section 4 Exclusion Rules), generate content hashes, extract metadata, and save this data to `workspace_index.json`.
* **If `workspace_index.json` exists:** The agent must load this file to resolve file paths, dependencies, and context. DO NOT perform a fresh directory scan.

#### 2.1 The Hash Check (The Analysis Gatekeeper)
When asked to analyze, reference, or modify a specific file, the agent must first compare the file's current content hash (e.g., MD5 or SHA-256) against the stored `hash` in the index:
* **Match (Unchanged):** Do NOT read the full file. Rely entirely on the stored `summary` and `analysis_cache` to understand its logic, exports, and purpose.
* **Mismatch (Changed/New):** Read the full file, perform a deep analysis, and immediately update its `hash`, `summary`, and `analysis_cache` in `workspace_index.json`.

### 3. Data Structure
The index must be formatted as a JSON object, acting as a key-value hash table for fast lookups, state management, and dependency tracking.

```json
{
  "last_updated": "YYYY-MM-DD",
  "files": {
    "filename.ext": {
      "path": "relative/path/to/filename.ext",
      "hash": "a1b2c3d4e5f6g7h8i9j0...",
      "summary": "Brief 1-sentence description of the file's purpose.",
      "analysis_cache": {
        "exports": ["PublicFunction1", "ClassA"],
        "dependencies": ["library_x", "local_file_y"],
        "tags": ["#API", "#Database", "#UI"]
      }
    }
  }
}
```
