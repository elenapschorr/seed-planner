-- Nutrient demand and exposure for 42 plant types (values from the Russian review table).
BEGIN;
WITH v(name, nutrient_demand, exposure) AS (VALUES
  ('Apricot', 'Medium', 'FullSun'),
  ('French marigold', 'Light', 'FullSun'),
  ('Broad beans', 'Light', 'FullSun'),
  ('Borage', 'Light', 'FullSun'),
  ('Grapevine', 'Medium', 'FullSun'),
  ('Cherry', 'Medium', 'FullSun'),
  ('Blueberry', 'Light', 'FullSun'),
  ('Mustard', 'Light', 'FullSun'),
  ('Pear', 'Medium', 'FullSun'),
  ('Honeyberry', 'Light', 'FullSun'),
  ('Wild strawberry', 'Medium', 'PartialShade'),
  ('Pot marigold', 'Light', 'FullSun'),
  ('White cabbage', 'Heavy', 'FullSun'),
  ('Red cabbage', 'Heavy', 'FullSun'),
  ('Kiwi', 'Heavy', 'FullSun'),
  ('Strawberry', 'Medium', 'FullSun'),
  ('Cranberry', 'Light', 'FullSun'),
  ('Salsify', 'Medium', 'FullSun'),
  ('Garden cress', 'Light', 'PartialShade'),
  ('Welsh onion', 'Medium', 'FullSun'),
  ('Onion sets', 'Medium', 'FullSun'),
  ('Raspberry', 'Medium', 'FullSun'),
  ('Nasturtium', 'Light', 'FullSun'),
  ('Sunflower', 'Heavy', 'FullSun'),
  ('Wormwood', 'Light', 'FullSun'),
  ('May turnip', 'Medium', 'FullSun'),
  ('Rose', 'Heavy', 'FullSun'),
  ('Chamomile', 'Light', 'FullSun'),
  ('Lettuce', 'Medium', 'FullSun'),
  ('Batavia lettuce', 'Medium', 'FullSun'),
  ('Oak leaf lettuce', 'Medium', 'FullSun'),
  ('Winter head lettuce', 'Medium', 'FullSun'),
  ('Celtuce', 'Medium', 'FullSun'),
  ('European plum', 'Medium', 'FullSun'),
  ('Asparagus', 'Medium', 'FullSun'),
  ('Runner beans', 'Light', 'FullSun'),
  ('Horseradish', 'Heavy', 'FullSun'),
  ('Savory', 'Light', 'FullSun'),
  ('Garlic', 'Medium', 'FullSun'),
  ('Tarragon', 'Light', 'FullSun'),
  ('Apple', 'Medium', 'FullSun'),
  ('Sweet woodruff', 'Light', 'Shade')
)
UPDATE plant_types t SET nutrient_demand = v.nutrient_demand, exposure = v.exposure
FROM v WHERE t.name = v.name;

DO $$
DECLARE n int;
BEGIN
  SELECT count(*) INTO n FROM plant_types WHERE name IN ('Apricot', 'French marigold', 'Broad beans', 'Borage', 'Grapevine', 'Cherry', 'Blueberry', 'Mustard', 'Pear', 'Honeyberry', 'Wild strawberry', 'Pot marigold', 'White cabbage', 'Red cabbage', 'Kiwi', 'Strawberry', 'Cranberry', 'Salsify', 'Garden cress', 'Welsh onion', 'Onion sets', 'Raspberry', 'Nasturtium', 'Sunflower', 'Wormwood', 'May turnip', 'Rose', 'Chamomile', 'Lettuce', 'Batavia lettuce', 'Oak leaf lettuce', 'Winter head lettuce', 'Celtuce', 'European plum', 'Asparagus', 'Runner beans', 'Horseradish', 'Savory', 'Garlic', 'Tarragon', 'Apple', 'Sweet woodruff');
  IF n <> 42 THEN RAISE EXCEPTION 'Expected 42 plant types, found %', n; END IF;
END $$;
COMMIT;
