-- 1. THE TAG DATABASE (Configuration)
-- This mimics the "Point Builder" in real historians.
CREATE TABLE tags (
    tag_id SERIAL PRIMARY KEY,
    tag_name VARCHAR(100) UNIQUE NOT NULL, -- e.g., "PLC1.Tank.Pressure"
    description VARCHAR(255),              -- e.g., "Main Storage Tank Pressure"
    unit VARCHAR(20),                      -- e.g., "PSI", "Bar", "C"
    source_device VARCHAR(50),             -- e.g., "PLC_Node_1"
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 2. THE TIME-SERIES STORE (Process Data)
-- This mimics the high-speed data archive.
-- We use a Composite Key (timestamp + tag_id) for speed.
CREATE TABLE process_data (
    timestamp TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    tag_id INT REFERENCES tags(tag_id),
    value DOUBLE PRECISION NOT NULL,       -- The sensor reading
    quality INT DEFAULT 192,               -- OPC DA Standard: 192 = "Good", 0 = "Bad"
    PRIMARY KEY (timestamp, tag_id)
);

-- 3. INDEXING (For Fast Retrieval)
-- Historians are read-heavy (trends). We need an index on time.
CREATE INDEX idx_time ON process_data(timestamp DESC);

-- 4. SEED DATA (Default Tags)
-- Let's pre-load some tags so the system is ready to record.
INSERT INTO tags (tag_name, description, unit, source_device) VALUES 
('PLANT.PROCESS.PRESSURE', 'Main Line Pressure', 'PSI', 'PLC_1'),
('PLANT.PROCESS.TEMP', 'Reactor Temperature', 'Celsius', 'PLC_1'),
('PLANT.SAFETY.VALVE', 'Safety Release Valve Status', 'Bool', 'PLC_1');
